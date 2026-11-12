# Section 8 — Inventory Persistence with PostgreSQL

[Project home](../../README.md) · [Previous section](section-07-validation-errors-and-logging.md)

## Overview

The Spring Boot Inventory Service reads stock quantities from PostgreSQL through Spring Data JPA and Hibernate. Flyway manages the database schema. The service uses the `inventory` database and the `inventory_app` login, and Docker Compose exposes the API on `http://localhost:5081`.

The endpoint is `GET /api/inventory/{productId}`. Its response contains `productId` and `availableQuantity`. A product with zero stock returns `200 OK` with a quantity of `0`. A missing product returns `404 Not Found`, and an invalid ID returns `400 Bad Request`.

## How an inventory lookup works

```text
HTTP request
    → InventoryController
    → DatabaseInventoryService
    → InventoryRepository
    → Hibernate
    → PostgreSQL JDBC driver
    → PostgreSQL
```

1. `InventoryController` passes the requested ID to the `InventoryService` interface. Spring injects `DatabaseInventoryService` as its implementation.
2. The service rejects nonpositive IDs and calls `InventoryRepository.findById(productId)` for valid IDs.
3. Spring Data JPA supplies the repository implementation. Hibernate maps the matching database row to an `InventoryItem`.
4. The service converts the entity to the `InventoryResponse` DTO. If the lookup is empty, it throws `InventoryNotFoundException`, which the exception handler maps to `404`.

## Java and .NET technology comparison

| Purpose | Java Inventory Service | .NET Order API |
|---|---|---|
| **Start and configure the web application** | **Spring Boot** configures the application and starts embedded Tomcat. | **ASP.NET Core** configures the application and its HTTP pipeline. Kestrel serves requests. |
| **Route HTTP requests** | **Spring MVC** uses `@RestController` and `@GetMapping` to expose the inventory lookup. | **ASP.NET Core MVC** uses `[ApiController]`, `[Route]`, and `[HttpGet]` to expose endpoints such as product lookup. |
| **Supply dependencies** | **Spring dependency injection** supplies the inventory service and repository through constructors. | **.NET dependency injection** supplies services and repositories registered in `Program.cs`, such as `IProductService` and `IProductRepository`. |
| **Define the response** | `InventoryResponse` is a Java record that defines the JSON response fields. | A DTO such as `ProductDto` defines the fields returned to API callers. |
| **Map database rows to objects** | **Jakarta Persistence (JPA)** defines the mapping API and annotations. **Hibernate** implements them and maps the `inventory` table to `InventoryItem`. | **Entity Framework Core** maps database rows to C# entities using conventions, attributes, or Fluent API configuration. |
| **Query entities** | **Spring Data JPA** supplies repository methods such as `findById` through `JpaRepository<InventoryItem, Long>`. | **EF Core `DbContext` and `DbSet<T>`** provide methods such as `FindAsync`. The Order API uses repository classes to encapsulate its database queries. |
| **Execute SQL and read results** | **JDBC** defines the Java database access API. The **PostgreSQL JDBC driver** implements it for PostgreSQL. | **ADO.NET** defines the corresponding .NET database access APIs. **Npgsql** is the PostgreSQL ADO.NET provider used underneath the Npgsql EF Core provider. |
| **Pool database connections** | **HikariCP** manages and reuses JDBC connections. | **Npgsql connection pooling** manages and reuses PostgreSQL connections. |
| **Version database schema changes** | **Flyway** applies versioned SQL files and records them in `flyway_schema_history`. | **EF Core Migrations** can generate C# migrations from model changes. `dotnet ef database update` applies them and records them in `__EFMigrationsHistory`. |
| **Configure database access** | Spring Boot reads `SPRING_DATASOURCE_URL`, `SPRING_DATASOURCE_USERNAME`, and `SPRING_DATASOURCE_PASSWORD`. | ASP.NET Core reads settings such as `ConnectionStrings__DefaultConnection` through `IConfiguration`. |
| **Test the HTTP and data access layers** | **JUnit and Spring Boot Test** exercise the service and HTTP endpoints. The test profile uses in-memory **H2**. | **xUnit and `WebApplicationFactory`** exercise the ASP.NET Core request pipeline. The Order API integration tests use in-memory **SQLite**. |
| **Build and package** | The **Maven Wrapper** reads `pom.xml`. `.\mvnw.cmd clean verify` builds the application and runs its tests. | The **.NET CLI** reads `.csproj` and solution files. `dotnet build`, `dotnet test`, and `dotnet publish` cover compilation, testing, and packaging. |

### Example — Look up an entity by primary key

The Java service uses the inventory repository.

```java
var item = inventoryRepository.findById(productId)
        .orElseThrow(() -> new InventoryNotFoundException(productId));

return new InventoryResponse(
        item.getProductId(),
        item.getAvailableQuantity());
```

`findById` returns an `Optional<InventoryItem>`. The service builds the response from a matching entity or throws the existing exception when the result is empty.

The corresponding EF Core pattern, shown here with the Order API’s products, uses a `DbSet`.

```csharp
var product = await db.Products.FindAsync(
    new object?[] { productId },
    cancellationToken);
```

`FindAsync` returns the entity or `null` if the key is missing. For direct SQL, Java can use a JDBC `PreparedStatement` and `ResultSet`, while .NET can use an `NpgsqlCommand` and a data reader. Both let the developer write the SQL, bind parameters, and read the result explicitly.

## Database schema and migration

Flyway loads `src/main/resources/db/migration/V1__create_inventory_table.sql` at startup. Version `1` creates the `inventory` table and inserts three sample rows.

| Product ID | Initial available quantity |
|---:|---:|
| 1 | 100 |
| 2 | 50 |
| 3 | 0 |

`product_id` is a `BIGINT` primary key mapped to Java `Long`. `available_quantity` is a required integer with a constraint that rejects negative quantities.

Flyway records version `1` in `flyway_schema_history`. On later starts, it validates the migration and applies any pending versions. Future schema changes belong in a new migration file, such as `V2__add_inventory_location.sql`. Hibernate uses `spring.jpa.hibernate.ddl-auto=validate` to check that the schema matches the Java entity after Flyway prepares the database.

To inspect the applied migration, run this in the `inventory@localhost` SQL console.

```sql
SELECT version, description, success
FROM public.flyway_schema_history
ORDER BY installed_rank;
```

## Run locally with Docker Compose

1. Start PostgreSQL on the Windows host. Provision the `inventory` database and the `inventory_app` login. For this local setup, `inventory_app` owns the database so Flyway can create its tables.
2. Create `.env.inventory.local` beside `compose.yaml`. Replace the password placeholder with your local `inventory_app` password. Keep this credentials file excluded from Git.

   ```dotenv
   SPRING_DATASOURCE_URL=jdbc:postgresql://host.docker.internal:5432/inventory
   SPRING_DATASOURCE_USERNAME=inventory_app
   SPRING_DATASOURCE_PASSWORD='YOUR_LOCAL_INVENTORY_PASSWORD'
   ```

3. From the repository root, build and start the Inventory Service.

   ```powershell
   docker compose up -d --build inventory
   docker compose ps inventory
   docker compose logs --tail 80 inventory
   ```

The container uses `host.docker.internal:5432` to reach PostgreSQL on Windows. An IntelliJ SQL console running directly on Windows uses `localhost:5432` to reach the same database.

## Verify the API in Postman

Use `http://localhost:5081` as the base URL.

| Request | Expected response |
|---|---|
| `GET /api/inventory/1` | `200 OK`, with quantity `100` if the seed value is unchanged |
| `GET /api/inventory/3` | `200 OK`, with quantity `0` |
| `GET /api/inventory/999999` | `404 Not Found` when that ID is absent |
| `GET /api/inventory/0` | `400 Bad Request` |
| `GET /api/inventory/abc` | `400 Bad Request` |

For product `1`, the initial response is:

```json
{
  "productId": 1,
  "availableQuantity": 100
}
```

### Verify persistence across a container restart

1. In the `inventory@localhost` SQL console, read and record product `1`’s current quantity.

   ```sql
   SELECT available_quantity
   FROM public.inventory
   WHERE product_id = 1;
   ```

2. Set a different test quantity, such as `80`. Commit the update if the SQL console uses manual transactions.

   ```sql
   UPDATE public.inventory
   SET available_quantity = 80
   WHERE product_id = 1
   RETURNING product_id, available_quantity;
   ```

3. Send `GET /api/inventory/1` and confirm the response shows `80`.
4. Run `docker compose restart inventory`. Wait for `Started InventoryServiceApplication` in the logs and repeat the request. Expect the same quantity.
5. Restore the original quantity recorded in Step 1.

## Automated tests

From `services/inventory-service`, run:

```powershell
.\mvnw.cmd clean verify
```

The Spring Boot tests activate the `test` profile. Each application context gets its own in-memory H2 database. Spring runs the `V1` SQL to create and seed the test table, and Hibernate validates the mapping. Flyway is disabled in the H2 profile. The endpoint tests exercise the JPA lookup through HTTP, while the Docker checks exercise the service against PostgreSQL.

## Verification status

- Maven reported `BUILD SUCCESS` after the JPA and H2 changes.
- Docker logs confirmed a successful PostgreSQL connection, Flyway validation of version `1`, and Hibernate initialization.
- Record the Postman restart result and GitHub Actions result after those checks pass.

Stock reservations and integration with the Orders API are later milestones.

[Previous section](section-07-validation-errors-and-logging.md) · [Project home](../../README.md)