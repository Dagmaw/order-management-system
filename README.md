\# Order Management System



An ASP.NET Core backend project being developed toward an AWS-hosted order management application.



The current implementation provides a PostgreSQL-backed Product API, organized into Domain, Application, Infrastructure, and API projects.



\## Current Features



\- Create a product.

\- Retrieve all products.

\- Retrieve a product by ID.

\- Validate product creation requests.

\- Return a not-found response for missing products.

\- Persist products using Entity Framework Core and PostgreSQL.

\- Manage database schema changes through EF Core migrations.

\- Build and run the API using a multi-stage Docker image.



Authentication and authorization are not implemented yet. Run this version locally for development.



\## Technology Stack



\- .NET 10 and ASP.NET Core controllers.

\- Entity Framework Core 10.

\- Npgsql PostgreSQL provider.

\- Local PostgreSQL installation.

\- Docker Desktop with Linux containers.

\- Visual Studio 2026.



The repository's `global.json` selects .NET SDK `10.0.402`, allowing newer patches in the same SDK feature band.



\## Repository Structure



```text

order-management-system/

├── OrderManagementSystem.sln

├── global.json

├── README.md

├── .gitignore

├── .dockerignore

├── .config/

│   └── dotnet-tools.json

├── src/

│   ├── OrderManagement.API/

│   │   ├── Controllers/

│   │   │   └── ProductsController.cs

│   │   ├── Dockerfile

│   │   └── Program.cs

│   ├── OrderManagement.Application/

│   │   ├── DTOs/

│   │   ├── Interfaces/

│   │   └── Services/

│   ├── OrderManagement.Domain/

│   │   └── Entities/

│   └── OrderManagement.Infrastructure/

│       ├── Data/

│       │   ├── ApplicationDbContext.cs

│       │   └── Migrations/

│       └── Repositories/

├── tests/

└── docs/

```



The `tests` and `docs` folders are reserved for later work. Git does not track empty folders.



\## Architecture



```text

HTTP client

&#x20;   ↓

ProductsController

&#x20;   ↓

IProductService → ProductService

&#x20;   ↓

IProductRepository → ProductRepository

&#x20;   ↓

ApplicationDbContext

&#x20;   ↓

PostgreSQL

```



\- \*\*Domain:\*\* business entities.

\- \*\*Application:\*\* DTOs, service workflows, and repository contracts.

\- \*\*Infrastructure:\*\* EF Core context, migrations, and repository implementation.

\- \*\*API:\*\* HTTP endpoints and dependency registration.



Stock quantities and reservations are planned responsibilities of a separate Inventory Service.



\## Prerequisites



\- Docker Desktop running Linux containers.

\- A running PostgreSQL server accessible from Docker Desktop.

\- PostgreSQL's `psql` client.

\- PowerShell.



Run the following commands from the repository root.



\## 1. Prepare PostgreSQL



Connect using your PostgreSQL administrator account:



```powershell

psql -h localhost -p 5432 -U postgres -d postgres -W

```



For a fresh setup, create the application role:



```sql

CREATE ROLE ordermanagement\_app WITH LOGIN;

```



Set its password interactively:



```text

\\password ordermanagement\_app

```



Create the database:



```sql

CREATE DATABASE ordermanagement OWNER ordermanagement\_app;

```



Exit:



```text

\\q

```



If the role and database already exist, reuse them instead of recreating them.



The application role owns this local development database so it can apply migrations. Deployment permissions will be reviewed separately.



\## 2. Configure the Current PowerShell Session



Enter the application role's password when prompted:



```powershell

$databasePassword = Read-Host 'Enter the password for ordermanagement\_app' -AsSecureString

```



```powershell

$databaseCredential = \[System.Management.Automation.PSCredential]::new('ordermanagement\_app', $databasePassword)

```



```powershell

$env:PGPASSWORD = $databaseCredential.GetNetworkCredential().Password

```



```powershell

$env:ConnectionStrings\_\_DefaultConnection = 'Host=host.docker.internal;Port=5432;Database=ordermanagement;Username=ordermanagement\_app'

```



`host.docker.internal` addresses the laptop from inside Docker Desktop. Inside a container, `localhost` addresses the container itself.



Npgsql reads the password from `PGPASSWORD`. The password is temporarily available in process/container environment settings. Do not print these settings or commit credentials.



\## 3. Apply Existing Migrations



For a fresh database, run:



```powershell

docker run --rm --mount "type=bind,source=$($PWD.Path),target=/workspace" --workdir /workspace --env ASPNETCORE\_ENVIRONMENT=Development --env DOTNET\_ENVIRONMENT=Development --env ConnectionStrings\_\_DefaultConnection --env PGPASSWORD mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'dotnet tool restore \&\& dotnet restore src/OrderManagement.API/OrderManagement.API.csproj --force \&\& dotnet ef database update --project src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj --startup-project src/OrderManagement.API/OrderManagement.API.csproj --context ApplicationDbContext'

```



This applies the migrations committed to the repository. Do not generate another `InitialCreate` migration.



Close Visual Studio while running this source-mounted command to avoid competing Windows and Linux restores of shared build metadata.



\## 4. Build the Application Image



```powershell

docker build --file ./src/OrderManagement.API/Dockerfile --tag order-management-api:day1 .

```



The final `.` uses the repository root as the build context, making the referenced projects available.



The Dockerfile publishes the application in an SDK stage and copies the published files into an ASP.NET runtime stage. The final container runs as a non-root user.



\## 5. Run the API



Using the credentials configured above:



```powershell

try { docker run --rm --name ordermanagement-api-day1 --publish 127.0.0.1:5080:8080 --env ASPNETCORE\_ENVIRONMENT=Development --env DOTNET\_ENVIRONMENT=Development --env ConnectionStrings\_\_DefaultConnection --env PGPASSWORD order-management-api:day1 } finally { Remove-Item Env:\\PGPASSWORD,Env:\\ConnectionStrings\_\_DefaultConnection -ErrorAction SilentlyContinue; Remove-Variable databasePassword,databaseCredential -ErrorAction SilentlyContinue }

```



Leave this terminal running. Test requests from a second terminal.



The API is available at `http://localhost:5080`.



This local run uses the Development environment and HTTP. The image is compiled in Release configuration. Build configuration and runtime environment are separate settings.



Stop the container with Ctrl+C, or from another terminal:



```powershell

docker stop ordermanagement-api-day1

```



The container is removed after stopping. PostgreSQL data remains in the separate local database.



If setup fails before the run command, clear the temporary credentials manually:



```powershell

Remove-Item Env:\\PGPASSWORD,Env:\\ConnectionStrings\_\_DefaultConnection -ErrorAction SilentlyContinue

```



```powershell

Remove-Variable databasePassword,databaseCredential -ErrorAction SilentlyContinue

```



\## API Endpoints



| Method | Route | Expected success response |

|---|---|---|

| GET | `/api/products` | `200 OK` |

| GET | `/api/products/{id}` | `200 OK` |

| POST | `/api/products` | `201 Created` |



A missing product returns `404 Not Found`. Invalid creation input returns `400 Bad Request`.



\### Create a Product



```powershell

$productBody = @{ name = 'Laptop'; description = 'Development laptop'; price = 1200.00 } | ConvertTo-Json

```



```powershell

$createResponse = Invoke-WebRequest -Uri 'http://localhost:5080/api/products' -Method Post -ContentType 'application/json' -Body $productBody -UseBasicParsing

```



```powershell

$createResponse.StatusCode

```



```powershell

$createResponse.Content

```



```powershell

$createResponse.Headers\['Location']

```



Each successful POST creates a new product. Repeating the request creates additional rows.



\### List Products



```powershell

Invoke-RestMethod -Uri 'http://localhost:5080/api/products'

```



\### Retrieve the Created Product



```powershell

$createdProduct = $createResponse.Content | ConvertFrom-Json

```



```powershell

Invoke-RestMethod -Uri "http://localhost:5080/api/products/$($createdProduct.id)"

```



\### Creation Rules



\- Name is required and limited to 200 characters.

\- Description is limited to 2,000 characters.

\- Price must be between `0.01` and `9999999999999999.99`.

\- ID and creation time are supplied by the database/application.



These request rules are enforced through DTO validation at the API boundary.



\## OpenAPI



In Development, the generated API description is available at:



```text

http://localhost:5080/openapi/v1.json

```



Swagger UI is not currently configured.



\## Verification



Manual checks cover:



\- Product creation and retrieval.

\- Blank-name and negative-price rejection.

\- Missing-product responses.

\- Product persistence after API container restart.

\- Retrieval through the packaged application image.



Automated unit and integration tests are planned.



\## Planned Work



\- Customers, orders, and order items.

\- Authentication and role-based authorization.

\- Spring Boot Inventory Service.

\- Automated tests.

\- Structured logging and dependency health checks.

\- Docker Compose configuration.

\- AWS deployment, Terraform, and GitHub Actions.


# Day 2

## Day 2 - Product Updates, Deletion, and Automated Tests

### Completed Work

- Added product updates through the controller, service, and repository layers.
- Added product deletion through the same layers.
- Verified successful updates and deletion using Postman.
- Verified invalid update requests return validation errors.
- Verified missing products return `404 Not Found`.
- Added Docker Compose configuration for local startup.
- Moved local database credentials into an excluded `.env.local` file.
- Created the `OrderManagement.Application.Tests` xUnit project.
- Added four passing ProductService unit test cases.

### Local Architecture

The API runs in Docker. PostgreSQL runs directly on the Windows computer.

```text
Postman
   |
   v
localhost:5080
   |
   v
Docker API container:8080
   |
   v
host.docker.internal:5432
   |
   v
Local PostgreSQL database
```

The API continues to use the existing layered architecture:

```text
ProductsController
   |
   v
ProductService
   |
   v
ProductRepository
   |
   v
ApplicationDbContext
   |
   v
PostgreSQL
```

### Local Configuration

Create `.env.local` in the repository root, beside the solution file and
`compose.yaml`.

Use the following structure, replacing the password placeholder locally:

```dotenv
ASPNETCORE_ENVIRONMENT=Development
DOTNET_ENVIRONMENT=Development
ConnectionStrings__DefaultConnection=Host=host.docker.internal;Port=5432;Database=ordermanagement;Username=ordermanagement_app
PGPASSWORD='YOUR_EXISTING_DATABASE_PASSWORD'
```

The password is for the existing `ordermanagement_app` PostgreSQL role.

The file stores credentials in plain text for local development. Do not commit
or share it.

Ensure both `.gitignore` and `.dockerignore` contain:

```text
.env.local
```

A new clone requires its own local `.env.local` file.

### Docker Compose Configuration

The repository root contains `compose.yaml`:

```yaml
services:
  api:
    image: order-management-api:day2
    build:
      context: .
      dockerfile: src/OrderManagement.API/Dockerfile
    ports:
      - "127.0.0.1:5080:8080"
    env_file:
      - .env.local
```

PostgreSQL is not managed by this Compose configuration. The existing local
PostgreSQL server must be running.

### Start the Application

Start Docker Desktop, then run the following from the repository root:

```powershell
docker compose up -d --build
```

This builds the image and starts the API in the background.

The API is available at:

```text
http://localhost:5080
```

Check container status:

```powershell
docker compose ps
```

View API logs:

```powershell
docker compose logs -f api
```

Press `Ctrl+C` to stop following logs. This does not stop the background
container.

Stop and remove the Compose containers and network:

```powershell
docker compose down
```

The PostgreSQL data remains in the separate local Windows database.

After changing application code, rebuild and recreate the API with:

```powershell
docker compose up -d --build
```

Building in Visual Studio alone does not update the running Docker image.

### Product Endpoints

| Method | Route | Successful response | Other expected responses |
|---|---|---|---|
| GET | `/api/products` | `200 OK` | — |
| GET | `/api/products/{id}` | `200 OK` | `404 Not Found` |
| POST | `/api/products` | `201 Created` | `400 Bad Request` |
| PUT | `/api/products/{id}` | `200 OK` | `400 Bad Request`, `404 Not Found` |
| DELETE | `/api/products/{id}` | `204 No Content` | `404 Not Found` |

### Update a Product

Use Postman to send:

```http
PUT http://localhost:5080/api/products/{id}
Content-Type: application/json
```

Replace `{id}` with an existing product ID.

Example JSON body:

```json
{
  "name": "Day 2 Updated Product",
  "description": "Updated through PUT",
  "price": 35.00
}
```

Expected behavior:

- Updates the existing product's name, description, and price.
- Trims surrounding whitespace from the name and description.
- Preserves the product ID and creation timestamp.
- Returns `200 OK` with the updated product.
- Returns `404 Not Found` when the product does not exist.
- Returns `400 Bad Request` when the request fails DTO validation.

The PUT request supplies all editable fields.

### Delete a Product

Use Postman to send:

```http
DELETE http://localhost:5080/api/products/{id}
```

No request body is required.

Expected behavior:

- Permanently removes the existing product from the database.
- Returns `204 No Content` with an empty response body.
- Returns `404 Not Found` when the product does not exist.
- A subsequent GET for the deleted product returns `404 Not Found`.
- Deleting the same product again returns `404 Not Found`.

Use a dedicated test product when verifying deletion.

### Postman Organization

Collection:

```text
Order Management API
├── 01 - Products
│   ├── 01 - Get All Products
│   ├── 02 - Create Product
│   ├── 03 - Update Product
│   ├── 04 - Get Product By ID
│   └── 05 - Delete Product
├── 02 - Users
├── 03 - Orders
└── 04 - Validation Tests
    ├── 01 - Update Product - Blank Name
    ├── 02 - Update Product - Negative Price
    ├── 03 - Update Product - Not Found
    └── 04 - Delete Product - Not Found
```

Users and Orders folders are reserved for later implementation.

The API currently requires no HTTP authentication. Database credentials belong
in the API configuration, not in Postman authorization settings.

### Manual Verification Results

The following checks passed:

| Check | Observed result |
|---|---|
| List existing products | `200 OK` |
| Create a test product | `201 Created` |
| Update the test product | `200 OK` |
| Retrieve the updated product | Updated values returned |
| Update with a blank name | `400 Bad Request` |
| Update with a negative price | `400 Bad Request` |
| Update a missing product | `404 Not Found` |
| Delete the test product | `204 No Content` |
| Retrieve the deleted product | `404 Not Found` |
| List products after deletion | Deleted product absent; other products remain |
| Delete the product again | `404 Not Found` |

### Automated Unit Tests

Test project:

```text
tests/OrderManagement.Application.Tests
```

Project references:

- `OrderManagement.Application`
- `OrderManagement.Domain`

The tests use a stub implementation of `IProductRepository`. They do not
connect to PostgreSQL or require Docker.

Current coverage:

| Test case | Behavior checked |
|---|---|
| Successful update | Trims input and maps the saved product to a DTO |
| Missing-product update | Returns `null` |
| Successful deletion | Returns `true` |
| Missing-product deletion | Returns `false` |

The deletion theory executes twice, producing four test cases in total.

Latest verified result:

```text
4 passed
0 failed
0 skipped
```

#### Run Tests in Visual Studio

1. Open the solution.
2. Select **Test > Test Explorer**.
3. Click **Run All Tests**.
4. Confirm all four test cases pass.

These unit tests cover service behavior. They do not verify database persistence,
HTTP routing, or automatic API validation. Those behaviors were checked manually
through Postman.

Automated integration tests and additional validation coverage remain planned.

### Day 2 Completion Checklist

- [x] Existing API and PostgreSQL connectivity verified.
- [x] Product update endpoint implemented.
- [x] Update success and error responses verified.
- [x] Product deletion endpoint implemented.
- [x] Deletion success and missing-product responses verified.
- [x] Docker Compose startup configured.
- [x] Updated API image built and tested.
- [x] Four ProductService unit test cases passing.
- [ ] README updated and reviewed.
- [ ] Credentials exclusion verified.
- [ ] Day 2 changes committed.

### Next: Day 3

- Define User, Order, and OrderItem entities.
- Configure relationships and database migrations.
- Implement basic user and order operations.
- Calculate order totals on the server.
- Add tests for order behavior.

