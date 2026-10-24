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



These are planned features, not current capabilities.

