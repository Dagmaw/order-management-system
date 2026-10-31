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


## Day 3 - Users, Orders, and Order Items

### Overview

Day 3 extends the Order Management System with basic user management and
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

### Technology Stack

- .NET 10 and ASP.NET Core controllers.
- Entity Framework Core.
- Npgsql PostgreSQL provider.
- PostgreSQL installed on Windows.
- Docker Desktop and Docker Compose.
- xUnit for unit tests.
- Moq for order-service test dependencies.
- Postman for manual API verification.
- pgAdmin for database inspection.

### Project Structure

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

### Architecture and Responsibilities

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

### Object-Oriented Design

#### Classes and Objects

Entities represent application concepts.

For example, `Order` is a class. Each `new Order()` creates an individual
order object with its own user, items, timestamp, and total.

#### Abstraction

Interfaces such as `IOrderService` and `IOrderRepository` define contracts.

The controller uses `IOrderService` without needing to know how orders are
validated or saved.

#### Encapsulation

Database operations are grouped inside repositories. Order validation and
total calculations are grouped inside the order service.

Entities currently use public setters. They are simple data models rather
than fully encapsulated domain models.

#### Composition

An Order contains a collection of OrderItems.

OrderService also uses repository dependencies supplied through its
constructor.

#### Polymorphism

A repository interface can be implemented by a PostgreSQL-backed repository
or replaced by a mock during testing.

The service works through the same interface in either case.

#### Inheritance

Controllers inherit framework behavior from `ControllerBase`.

Application exceptions inherit from `Exception`.

#### Dependency Injection

The application registers the implementations in `Program.cs`:

```csharp
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
```

Existing Product registrations remain in place.

### Database Model

```text
User
 └── Orders
      └── OrderItems
           └── Product
```

#### Users

| Field | Purpose |
|---|---|
| Id | Generated primary key |
| Name | User's display name, maximum 200 characters |
| Email | Normalized email, maximum 320 characters |
| CreatedAt | UTC creation timestamp |

Email has a unique database index.

This basic User entity does not store passwords.

#### Orders

| Field | Purpose |
|---|---|
| Id | Generated primary key |
| UserId | Foreign key to Users |
| CreatedAt | UTC creation timestamp |
| TotalAmount | Server-calculated total, stored as `numeric(18,2)` |

#### OrderItems

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

#### Relationships and Deletion Rules

| Relationship | Rule |
|---|---|
| User → Orders | Restrict deletion of users referenced by orders |
| Order → OrderItems | Cascade deletion of items when an order is deleted |
| Product → OrderItems | Restrict deletion of products referenced by items |

A unique index on `(OrderId, ProductId)` prevents duplicate product rows
within one order.

There is currently no public order-deletion endpoint.

### Database Migration

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

### Docker Compose Configuration

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

### Local Credentials

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

### Start, Stop, and Inspect the Application

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

### Apply Existing Migrations Through Compose

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

### Users API

| Method | Route | Success | Expected errors |
|---|---|---|---|
| POST | `/api/users` | `201 Created` | `400`, `409` |
| GET | `/api/users` | `200 OK` | |
| GET | `/api/users/{id}` | `200 OK` | `404` |

#### Create a User

```http
POST http://localhost:5080/api/users
Content-Type: application/json
```

```json
{
  "name": "Day 3 Test User",
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

#### Retrieve Users

```http
GET http://localhost:5080/api/users
```

```http
GET http://localhost:5080/api/users/{id}
```

Replace `{id}` with an actual user ID.

A missing user returns `404 Not Found`.

### Orders API

| Method | Route | Success | Expected errors |
|---|---|---|---|
| POST | `/api/orders` | `201 Created` | `400`, `409` |
| GET | `/api/orders` | `200 OK` | |
| GET | `/api/orders/{id}` | `200 OK` | `404` |

#### Create an Order

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

#### Order Validation

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

#### Price Calculation

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

#### Saving an Order

The repository adds the Order and its Items, then calls `SaveChangesAsync`
once.

The inserts execute in a database transaction. A failure prevents a partially
saved order.

Catalog reads occur before that save transaction. This version does not lock
catalog prices or reserve inventory.

#### Retrieve Orders

```http
GET http://localhost:5080/api/orders
```

```http
GET http://localhost:5080/api/orders/{id}
```

Responses contain the order's user ID, timestamp, total, and item details.

Missing order IDs return `404 Not Found`.

### Product Deletion and Order History

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

### Postman Collection

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

### Manual Verification

Day 3 verification covers:

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

#### Inspect a Saved Order in pgAdmin

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

### Automated Tests

Test project:

```text
tests/OrderManagement.Application.Tests
```

The existing ProductService tests use a stub repository.

OrderService tests use Moq repository replacements and do not require
PostgreSQL or Docker.

#### OrderService Coverage

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

#### Run Tests

In Visual Studio:

1. Build the solution.
2. Open **Test > Test Explorer**.
3. Select **Run All Tests**.
4. Confirm all tests pass.

Record the observed result before committing the documentation.

Unit tests do not verify real PostgreSQL transactions, database constraints,
or HTTP routing. Those behaviors were checked manually. Automated integration
tests remain planned.

### Current Limitations

- No authentication or role-based authorization.
- No ownership checks restricting users to their own orders.
- User email data is exposed by the local development Users endpoints.
- No inventory lookup, reservation, or stock deduction.
- No payment processing.
- No tax, shipping, or discount calculations.
- No order update, cancellation, or deletion endpoints.
- No pagination on list endpoints.
- No catalog-price locking during order creation.
- No idempotency protection; repeating a successful POST creates another order.
- Automated integration tests remain planned.

### Day 3 Completion Checklist

- [x] Day 2 baseline verified.
- [x] User, Order, and OrderItem entities defined.
- [x] EF Core relationships and decimal precision configured.
- [x] AddUsersAndOrders migration applied and verified.
- [x] Users API implemented and manually verified.
- [x] Order creation implemented.
- [x] Order validation and server-side totals implemented.
- [x] Order listing and retrieval implemented.
- [x] Purchase-time price behavior verified.
- [x] Referenced-product deletion protection verified.
- [x] Docker application rebuilt and manually verified.
- [x] Order persistence after API restart verified.
- [ ] Automated test results confirmed and recorded.
- [ ] README reviewed against the current implementation.
- [ ] Credentials exclusion verified.
- [ ] Day 3 changes committed.
- [ ] Day 3 commit pushed.

### Suggested Commit Message

```text
Add users and orders APIs with price snapshots and order tests
```

## Day 4 - Spring Boot Inventory Service

### Completed Work

- Created a separate Java/Spring Boot Inventory Service.
- Added inventory lookup using immutable sample data.
- Implemented a REST controller and centralized exception handling.
- Added service unit tests and HTTP endpoint tests.
- Built and ran the service through Docker Compose.
- Verified successful, missing-product, and invalid-ID responses in Postman.
- Verified service behavior after container restart.
- Confirmed the existing .NET API continues to work.
- Reviewed Terraform providers, resources, variables, outputs, state,
  and the init/validate/plan/apply workflow.

### Technology Stack

| Component | Configuration |
|---|---|
| Java | 25 |
| Spring Boot | 4.1.2 |
| Maven | 3.10.0 through Maven Wrapper |
| IDE | IntelliJ IDEA |
| Tests | JUnit and Spring Boot testing |
| Container runtime | Eclipse Temurin Java 25 |
| Local orchestration | Docker Compose |
| Manual API testing | Postman |

### Project Location

```text
services/inventory-service
```

The Java project is managed through Maven and IntelliJ.
The existing .NET projects remain managed through the solution file.

### Local Architecture

```text
Postman
   |
   +--> localhost:5080 --> .NET Order API --> PostgreSQL
   |
   +--> localhost:5081 --> Spring Inventory Service --> Sample map
```

The services run independently. The .NET API does not call the Inventory
Service yet.

### Inventory Application Structure

```text
com.dagmawi.inventory
├── InventoryServiceApplication
├── controller
│   └── InventoryController
├── dto
│   └── InventoryResponse
├── exception
│   ├── ApiExceptionHandler
│   ├── InvalidProductIdException
│   └── InventoryNotFoundException
└── service
    ├── InventoryService
    └── InMemoryInventoryService
```

### Object-Oriented Design

- **Abstraction:** InventoryService defines the lookup contract.
- **Encapsulation:** InMemoryInventoryService hides its private sample map.
- **Polymorphism:** The controller uses the InventoryService interface.
- **Dependency injection:** Spring supplies the service implementation
  through the controller constructor.
- **Inheritance:** Custom exceptions extend RuntimeException.
- **Immutable data:** InventoryResponse is a record containing the
  product ID and available quantity.

HTTP responsibilities remain in the controller and exception handler.
Inventory lookup behavior remains in the service.

### Sample Inventory

| Product ID | Available quantity |
|---|---:|
| 1 | 100 |
| 2 | 50 |
| 3 | 0 |

These fixtures are independent of the .NET Products table.

A product with zero stock exists and returns a successful lookup.
An unknown product returns a not-found response.

### Inventory Endpoint

```http
GET http://localhost:5081/api/inventory/{productId}
```

Example:

```http
GET http://localhost:5081/api/inventory/1
```

Response: `200 OK`

```json
{
  "productId": 1,
  "availableQuantity": 100
}
```

### Error Handling

| Input | Expected response |
|---|---|
| Known product | 200 OK |
| Known product with zero stock | 200 OK |
| Unknown positive product ID | 404 Not Found |
| Zero or negative product ID | 400 Bad Request |
| Nonnumeric product ID | 400 Bad Request |

ApiExceptionHandler uses @RestControllerAdvice and @ExceptionHandler to
translate custom exceptions into ProblemDetail responses.

Example not-found response fields:

```json
{
  "title": "Inventory not found",
  "status": 404,
  "detail": "Inventory was not found for product 999999."
}
```

Additional framework-generated fields may be present.

### Run Tests Locally

From the Inventory project directory:

```powershell
.\mvnw.cmd clean verify
```

Verified result:

```text
Tests run: 9
Failures: 0
Errors: 0
Skipped: 0
BUILD SUCCESS
```

Coverage includes:

- Application context startup.
- Known inventory lookup.
- Zero-stock lookup.
- Unknown inventory.
- Invalid numeric IDs.
- HTTP response serialization.
- Structured not-found responses.
- Invalid path-variable handling.

### Docker Build

The multi-stage Dockerfile:

1. Uses a Java 25 JDK.
2. Builds with the project's Maven Wrapper.
3. Runs tests during the build.
4. Packages an executable Spring Boot JAR.
5. Copies the JAR into a Java 25 runtime image.
6. Runs the application as a non-root user.

Image tag:

```text
inventory-service:day4
```

### Start Inventory Through Compose

Run from the repository root:

```powershell
docker compose up -d --build inventory
```

Check status:

```powershell
docker compose ps inventory
```

View logs:

```powershell
docker compose logs --tail 50 inventory
```

Restart:

```powershell
docker compose restart inventory
```

Windows port 5081 maps to port 8080 inside the Inventory container.

After source changes, rebuild the image. Restarting alone does not
package changed source code.

### Manual Verification

The following behaviors were verified through Postman:

- Known product returns the expected inventory.
- Zero-stock product returns quantity 0.
- Unknown product returns 404.
- Zero, negative, and nonnumeric IDs return 400.
- Lookup still works after container restart.
- The .NET Products endpoint remains available on port 5080.

Restarting reconstructs the sample map. This is not database persistence.

### Terraform Study

Reviewed:

- Providers and platform API communication.
- Resources and desired infrastructure.
- Variables and environment-specific inputs.
- Outputs for people and automation.
- State and managed-resource identity.
- Initialization, validation, planning, and application.

No AWS resources were deployed as part of Day 4.

### Current Limitations

- Inventory is read-only sample data.
- No Inventory database or JPA persistence.
- No stock update, reservation, or release endpoints.
- Sample IDs are not synchronized with the .NET catalog.
- No communication between the Order API and Inventory Service.
- No authentication or authorization.
- No cloud deployment yet.

### Next Work

- Complete the Week 1 review and any remaining milestones.
- Begin Inventory persistence in Week 2.
- Add security and cross-service integration during later plan stages.
