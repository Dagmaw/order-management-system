# Section 3 — Users, Orders, and OrderItems

[← Project home](../../README.md)

> This page records the implementation at this milestone. For current startup instructions and access rules, see the project home page and Section 6. Historical image tags and sample data are preserved.

### Overview ###

Section 3 extends the Order Management System with basic user management and
order processing.

An order belongs to a user and contains one or more order items. Product
prices are read by the server when the order is created and copied into
the order items.

The application now supports:

- Creating and retrieving users.
- Normalizing user emails and rejecting duplicates.
- Creating orders for existing users and products.
- Validating quantities and rejecting duplicate products within an order.
- Calculating line totals and order totals on the server.
- Preserving purchase-time unit prices.
- Listing orders and retrieving an order with its items.
- Preventing deletion of products referenced by orders.
- Returning clear validation, not-found, and conflict responses.

Authentication, authorization, inventory reservation, and payment processing
are not implemented. Run this application locally for development.

### Technology Stack ###

- .NET 10 and ASP.NET Core controllers.
- Entity Framework Core.
- Npgsql PostgreSQL provider.
- PostgreSQL installed on Windows.
- Docker Desktop and Docker Compose.
- xUnit for unit tests.
- Moq for order-service test dependencies.
- Postman for manual API verification.
- pgAdmin for database inspection.

### Project Structure ###

```text
order-management-system/
├── OrderManagementSystem.sln
├── compose.yaml
├── .env.local                     # Local only; excluded from Git/builds
├── .gitignore
├── .dockerignore
├── README.md
├── src/
│   ├── OrderManagement.API/
│   │   ├── Controllers/
│   │   │   ├── ProductsController.cs
│   │   │   ├── UsersController.cs
│   │   │   └── OrdersController.cs
│   │   ├── Dockerfile
│   │   └── Program.cs
│   ├── OrderManagement.Application/
│   │   ├── DTOs/
│   │   │   ├── CreateUserDto.cs
│   │   │   ├── UserDto.cs
│   │   │   ├── CreateOrderDto.cs
│   │   │   ├── CreateOrderItemDto.cs
│   │   │   ├── OrderDto.cs
│   │   │   └── OrderItemDto.cs
│   │   ├── Exceptions/
│   │   │   ├── DuplicateEmailException.cs
│   │   │   ├── OrderValidationException.cs
│   │   │   └── DataConflictException.cs
│   │   ├── Interfaces/
│   │   │   ├── IUserRepository.cs
│   │   │   ├── IUserService.cs
│   │   │   ├── IOrderRepository.cs
│   │   │   └── IOrderService.cs
│   │   └── Services/
│   │       ├── ProductService.cs
│   │       ├── UserService.cs
│   │       └── OrderService.cs
│   ├── OrderManagement.Domain/
│   │   └── Entities/
│   │       ├── Product.cs
│   │       ├── User.cs
│   │       ├── Order.cs
│   │       └── OrderItem.cs
│   └── OrderManagement.Infrastructure/
│       ├── Data/
│       │   ├── ApplicationDbContext.cs
│       │   └── Migrations/
│       └── Repositories/
│           ├── ProductRepository.cs
│           ├── UserRepository.cs
│           └── OrderRepository.cs
└── tests/
    └── OrderManagement.Application.Tests/
        ├── ProductServiceTests.cs
        └── OrderServiceTests.cs
```

Existing Product DTOs and interfaces are omitted from this abbreviated tree.

### Architecture and Responsibilities ###

```text
Postman
   |
   v
ASP.NET Core controller
   |
   v
Application service
   |
   v
Repository interface
   |
   v
EF Core repository
   |
   v
ApplicationDbContext
   |
   v
PostgreSQL
```

| Layer | Responsibility |
|---|---|
| Domain | Defines Product, User, Order, and OrderItem entities |
| Application | Defines DTOs, business workflows, interfaces, and application exceptions |
| Infrastructure | Implements database access, migrations, and persistence |
| API | Handles HTTP requests, dependency registration, and HTTP responses |
| Tests | Verifies service behavior using controlled repository replacements |

### Object-Oriented Design ###

#### Classes and Objects ####

Entities represent application concepts.

For example, `Order` is a class. Each `new Order()` creates an individual
order object with its own user, items, timestamp, and total.

#### Abstraction ####

Interfaces such as `IOrderService` and `IOrderRepository` define contracts.

The controller uses `IOrderService` without needing to know how orders are
validated or saved.

#### Encapsulation ####

Database operations are grouped inside repositories. Order validation and
total calculations are grouped inside the order service.

Entities currently use public setters. They are simple data models rather
than fully encapsulated domain models.

#### Composition ####

An Order contains a collection of OrderItems.

OrderService also uses repository dependencies supplied through its
constructor.

#### Polymorphism ####

A repository interface can be implemented by a PostgreSQL-backed repository
or replaced by a mock during testing.

The service works through the same interface in either case.

#### Inheritance ####

Controllers inherit framework behavior from `ControllerBase`.

Application exceptions inherit from `Exception`.

#### Dependency Injection ####

The application registers the implementations in `Program.cs`:

```csharp
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
```

Existing Product registrations remain in place.

### Database Model ###

```text
User
 └── Orders
      └── OrderItems
           └── Product
```

#### Users ####

| Field | Purpose |
|---|---|
| Id | Generated primary key |
| Name | User's display name, maximum 200 characters |
| Email | Normalized email, maximum 320 characters |
| CreatedAt | UTC creation timestamp |

Email has a unique database index.

This basic User entity does not store passwords.

#### Orders ####

| Field | Purpose |
|---|---|
| Id | Generated primary key |
| UserId | Foreign key to Users |
| CreatedAt | UTC creation timestamp |
| TotalAmount | Server-calculated total, stored as `numeric(18,2)` |

#### OrderItems ####

| Field | Purpose |
|---|---|
| Id | Generated primary key |
| OrderId | Foreign key to Orders |
| ProductId | Foreign key to Products |
| Quantity | Ordered quantity |
| UnitPrice | Purchase-time price, stored as `numeric(18,2)` |

`LineTotal` is calculated as:

```text
Quantity × UnitPrice
```

It is not stored as a separate database column.

#### Relationships and Deletion Rules ####

| Relationship | Rule |
|---|---|
| User → Orders | Restrict deletion of users referenced by orders |
| Order → OrderItems | Cascade deletion of items when an order is deleted |
| Product → OrderItems | Restrict deletion of products referenced by items |

A unique index on `(OrderId, ProductId)` prevents duplicate product rows
within one order.

There is currently no public order-deletion endpoint.

### Database Migration ###

The following migrations have been applied:

```text
20261023225430_InitialCreate
20261028010946_AddUsersAndOrders
```

`AddUsersAndOrders` creates:

- Users.
- Orders.
- OrderItems.
- Required foreign keys.
- A unique email index.
- A unique Order/Product index.

The existing Products table is preserved.

Verify migration history in pgAdmin:

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";
```

Verify tables:

```sql
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
ORDER BY table_name;
```

Expected tables:

```text
OrderItems
Orders
Products
Users
__EFMigrationsHistory
```

### Docker Compose Configuration ###

The API runs in Docker. PostgreSQL runs directly on Windows.

```yaml
services:
  api:
    image: order-management-api:day3
    build:
      context: .
      dockerfile: src/OrderManagement.API/Dockerfile
    ports:
      - "127.0.0.1:5080:8080"
    env_file:
      - .env.local

  tools:
    image: mcr.microsoft.com/dotnet/sdk:10.0
    profiles:
      - tools
    working_dir: /workspace
    volumes:
      - .:/workspace
    env_file:
      - .env.local
```

The `api` service runs the packaged application.

The `tools` service provides the .NET SDK for migration commands. Its profile
keeps it out of normal application startup.

The `day3` tag identifies the local application image. Rebuilding replaces the
image associated with that tag.

### Local Credentials ###
 
Create `.env.local` in the repository root:

```dotenv
ASPNETCORE_ENVIRONMENT=Development
DOTNET_ENVIRONMENT=Development
ConnectionStrings__DefaultConnection=Host=host.docker.internal;Port=5432;Database=ordermanagement;Username=ordermanagement_app
PGPASSWORD='YOUR_EXISTING_DATABASE_PASSWORD'
```

Replace the password placeholder locally.

`host.docker.internal` addresses the Windows host from inside Docker Desktop.
Inside a container, `localhost` addresses that container.

The environment file stores credentials in plain text. Keep it private.

Both `.gitignore` and `.dockerignore` must exclude:

```text
.env.local
```

Do not place the actual password in source code, the Dockerfile, Compose
configuration, README examples, or Postman requests.

### Start, Stop, and Inspect the Application ###

Run commands from the repository root.

Build and start the API:

```powershell
docker compose up -d --build api
```

Check status:

```powershell
docker compose ps
```

Inspect image tags:

```powershell
docker compose images
```

Follow logs:

```powershell
docker compose logs -f api
```

Press `Ctrl+C` to stop following logs without stopping the background API.

Restart the API:

```powershell
docker compose restart api
```

Stop and remove Compose containers and the network:

```powershell
docker compose down
```

The separate Windows PostgreSQL database remains intact.

After source changes, use `up -d --build api`. Restarting alone does not package
new source code.

### Apply Existing Migrations Through Compose ###

For a database that needs the migrations already committed to this repository,
use the SDK tools service.

Save and close Visual Studio before source-mounted Docker builds to avoid
Windows and Linux modifying shared build metadata simultaneously.

```powershell
docker compose run --rm tools sh -c 'dotnet tool install dotnet-ef --tool-path /tmp/ef --version 10.0.13 && dotnet restore src/OrderManagement.API/OrderManagement.API.csproj --force && /tmp/ef/dotnet-ef database update AddUsersAndOrders --project src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj --startup-project src/OrderManagement.API/OrderManagement.API.csproj --context ApplicationDbContext'
```

This applies existing migrations up to `AddUsersAndOrders`. It does not
generate a new migration.

The command requires the database, application role, and `.env.local`
configuration to exist.

### Users API ###

| Method | Route | Success | Expected errors |
|---|---|---|---|
| POST | `/api/users` | `201 Created` | `400`, `409` |
| GET | `/api/users` | `200 OK` | |
| GET | `/api/users/{id}` | `200 OK` | `404` |

#### Create a User ####

```http
POST http://localhost:5080/api/users
Content-Type: application/json
```

```json
{
  "name": "Section 3 Test User",
  "email": "day3.user@example.com"
}
```

Expected response: `201 Created`, with the generated user ID and timestamp.

User creation:

- Requires a name and valid email.
- Trims the name.
- Trims the email and converts it to lowercase.
- Rejects duplicate normalized emails with `409 Conflict`.
- Returns a DTO without entity navigation properties.

The service checks for existing emails. The database unique index provides
the final protection against simultaneous duplicate requests.

#### Retrieve Users ####

```http
GET http://localhost:5080/api/users
```

```http
GET http://localhost:5080/api/users/{id}
```

Replace `{id}` with an actual user ID.

A missing user returns `404 Not Found`.

### Orders API ###

| Method | Route | Success | Expected errors |
|---|---|---|---|
| POST | `/api/orders` | `201 Created` | `400`, `409` |
| GET | `/api/orders` | `200 OK` | |
| GET | `/api/orders/{id}` | `200 OK` | `404` |

#### Create an Order ####

```http
POST http://localhost:5080/api/orders
Content-Type: application/json
```

Example request:

```json
{
  "userId": 1,
  "items": [
    {
      "productId": 10,
      "quantity": 2
    },
    {
      "productId": 20,
      "quantity": 3
    }
  ]
}
```

Replace the example IDs with actual existing IDs.

The client supplies:

- User ID.
- Product IDs.
- Quantities.

Prices and totals are determined by the server. They are not accepted as
client-controlled order values.

#### Order Validation ####

An order must satisfy these rules:

- The user must exist.
- The order must contain between 1 and 100 items.
- Every product must exist.
- Quantities must be between 1 and 1,000.
- A product may appear only once in an order.
- Product prices must be positive and within the supported range.
- The calculated total must fit the configured database precision.

Invalid order requests return `400 Bad Request`.

If a referenced user or product disappears between validation and saving,
the repository translates the foreign-key failure into `409 Conflict`.

#### Price Calculation ####

For products priced at `25.00` and `10.00`:

| Item | Quantity | Unit price | Line total |
|---|---:|---:|---:|
| Product A | 2 | 25.00 | 50.00 |
| Product B | 3 | 10.00 | 30.00 |
| Total | | | 80.00 |

The service copies the current product price into each OrderItem's
`UnitPrice`.

Changing Product A to `30.00` later does not change the existing order's
saved unit price or total.

The current total excludes taxes, shipping, and discounts.

#### Saving an Order ####

The repository adds the Order and its Items, then calls `SaveChangesAsync`
once.

The inserts execute in a database transaction. A failure prevents a partially
saved order.

Catalog reads occur before that save transaction. This version does not lock
catalog prices or reserve inventory.

#### Retrieve Orders ####

```http
GET http://localhost:5080/api/orders
```

```http
GET http://localhost:5080/api/orders/{id}
```

Responses contain the order's user ID, timestamp, total, and item details.

Missing order IDs return `404 Not Found`.

### Product Deletion and Order History ###

Product deletion now has these outcomes:

| Situation | Response |
|---|---|
| Existing product with no order references | `204 No Content` |
| Missing product | `404 Not Found` |
| Product referenced by an order | `409 Conflict` |

Referenced products remain in the database.

The repository handles both PostgreSQL codes for the specific
`FK_OrderItems_Products_ProductId` constraint:

- `23503` — foreign-key violation.
- `23001` — restrict violation.

The error is translated into `DataConflictException`, which the controller
returns as a `409` problem response.

Example:

```json
{
  "title": "Product is referenced by an order",
  "status": 409,
  "detail": "This product cannot be deleted because it is referenced by an order."
}
```

The actual response also includes framework-generated fields.

### Postman Collection ###

```text
Order Management API
├── 01 - Products
│   ├── Get All Products
│   ├── Create Product
│   ├── Update Product
│   ├── Get Product By ID
│   └── Delete Product
├── 02 - Users
│   ├── Create User
│   ├── Get All Users
│   └── Get User By ID
├── 03 - Orders
│   ├── Create Order
│   ├── Get All Orders
│   └── Get Order By ID
└── 04 - Validation Tests
    ├── Create User - Duplicate Email
    ├── Create User - Invalid Input
    ├── Get User - Not Found
    ├── Create Order - Invalid Quantity
    ├── Create Order - Empty Items
    ├── Create Order - Missing User
    ├── Create Order - Missing Product
    ├── Create Order - Duplicate Products
    ├── Get Order - Not Found
    └── Delete Product - Referenced by Order
```

Use the actual IDs returned by creation requests.

The base address is:

```text
http://localhost:5080
```

No HTTP authentication is required in this local development version.

### Manual Verification ###

Section 3 verification covers:

- User creation, listing, and retrieval.
- Duplicate-email rejection.
- Invalid user-input rejection.
- Missing-user responses.
- Order creation with server-calculated totals.
- Order listing and retrieval with items.
- Invalid quantities and empty-item rejection.
- Missing user/product rejection during order creation.
- Duplicate-product rejection.
- Missing-order responses.
- Purchase-time prices remaining unchanged after a Product price update.
- Referenced-product deletion returning `409 Conflict`.
- The protected product remaining available afterward.
- Order persistence after API restart.

Rejected order requests must not create additional Orders or OrderItems.

#### Inspect a Saved Order in pgAdmin ####

Replace `100` with the actual order ID:

```sql
SELECT *
FROM "Orders"
WHERE "Id" = 100;
```

```sql
SELECT
    "OrderId",
    "ProductId",
    "Quantity",
    "UnitPrice",
    "Quantity" * "UnitPrice" AS "CalculatedLineTotal"
FROM "OrderItems"
WHERE "OrderId" = 100
ORDER BY "ProductId";
```

### Automated Tests ###

Test project:

```text
tests/OrderManagement.Application.Tests
```

The existing ProductService tests use a stub repository.

OrderService tests use Moq repository replacements and do not require
PostgreSQL or Docker.

#### OrderService Coverage ####

- Calculates totals across multiple products.
- Copies purchase-time unit prices into order items.
- Rejects quantities of zero, negative values, and values above 1,000.
- Rejects duplicate products.
- Rejects missing users.
- Rejects missing products.
- Rejects empty orders.
- Rejects totals exceeding database precision.
- Verifies invalid requests do not call the save operation.

The supplied OrderService test file contains nine test cases, including
parameterized quantity cases.

Together with the four existing ProductService cases, the expected total is
13, unless additional tests have been added.

#### Run Tests ####

In Visual Studio:

1. Build the solution.
2. Open **Test > Test Explorer**.
3. Select **Run All Tests**.
4. Confirm all tests pass.

---

[Previous section](../part-1/section-02-product-operations.md) | [Project home](../../README.md) | [Next section](../part-1/section-04-inventory-service.md)
