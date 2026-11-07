# Order Management System

A layered .NET Order Management API with PostgreSQL, a separate Spring Boot Inventory Service, Docker Compose, and GitHub Actions build/test automation.

## Current Features

- Products: creation, listing, retrieval, updates, and deletion with validation and conflict handling.
- Users: business user records linked to separate Identity login accounts.
- Orders: server-calculated totals, purchase-time price snapshots, and ownership checks.
- Authentication: Customer registration, JWT login, and Customer/Admin permissions.
- Inventory: independent Spring Boot lookup endpoint using in-memory sample data.
- Automated application tests and 11 API integration tests; .NET and Java CI jobs verified successfully.
- Product request validation with FluentValidation.
- Centralized Problem Details exception responses.
- Serilog JSON logging with request correlation IDs.

## Technology Stack

| Area | Technologies |
|---|---|
| Order API | C#, .NET 10, ASP.NET Core, Entity Framework Core 10 |
| Authentication | ASP.NET Core Identity, JWT Bearer authentication |
| Application database | PostgreSQL with Npgsql |
| Inventory | Java 25, Spring Boot 4.1.2, Maven Wrapper |
| Testing | xUnit, Moq, WebApplicationFactory, in-memory SQLite, JUnit |
| Containers and CI | Docker, Docker Compose, GitHub Actions |
| Infrastructure learning | Terraform; local terraform_data exercise |
| Development tools | Visual Studio, IntelliJ IDEA, Postman, DataGrip/pgAdmin |

Version details in section pages record their respective milestones; repository configuration is the source of truth for installed dependencies.

## Architecture

```text
Postman / HTTP client
    ├── localhost:5080 → ASP.NET Core API
    │                      → Application services
    │                      → Infrastructure / EF Core
    │                      → PostgreSQL on Windows host
    └── localhost:5081 → Spring Boot Inventory Service
                           → In-memory sample inventory
```

- **Domain:** business entities and relationships.
- **Application:** DTOs, service workflows, and contracts.
- **Infrastructure:** database access, migrations, and Identity implementation.
- **API:** endpoints, authentication, authorization, and dependency registration.

The services currently run independently; Orders does not call Inventory.

## Quick Start

Run commands from the repository root. Docker Desktop must be running Linux containers, and the existing local PostgreSQL database must be available.

1. Create your excluded `.env.local` beside `compose.yaml`. Supply your own database credentials and JWT configuration:

   ```dotenv
   ASPNETCORE_ENVIRONMENT=Development
   DOTNET_ENVIRONMENT=Development
   ConnectionStrings__DefaultConnection=Host=host.docker.internal;Port=5432;Database=ordermanagement;Username=ordermanagement_app
   PGPASSWORD=YOUR_DATABASE_PASSWORD
   Jwt__Issuer=OrderManagement.API
   Jwt__Audience=OrderManagement.Client
   Jwt__AccessTokenMinutes=15
   Jwt__SigningKeyBase64=YOUR_BASE64_SIGNING_KEY
   BootstrapAdmin__Enabled=false
   ```

   Replace both placeholders before startup. The signing key must decode to at least 32 random bytes. Keep credentials and tokens out of Git. See [initial database setup](docs/part-1/section-01-foundation.md) and [authentication configuration](docs/part-2/section-06-authentication.md).

2. Apply committed migrations to a new or outdated development database:

   ```powershell
   docker compose run --rm tools sh -c 'dotnet tool restore --tool-manifest dotnet-tools.json && dotnet restore OrderManagementSystem.sln --force && dotnet ef database update --project src/OrderManagement.Infrastructure --startup-project src/OrderManagement.API --context ApplicationDbContext'
   ```

3. Build and start both services:

   ```powershell
   docker compose up -d --build api inventory
   docker compose ps
   ```

| Service | Local address |
|---|---|
| Order Management API | http://localhost:5080 |
| Inventory Service | http://localhost:5081 |

PostgreSQL runs separately on the Windows host. Inside Docker, `host.docker.internal` points to that host.

## Authentication and Access

Register through `POST /api/auth/register`, log in through `POST /api/auth/login`, and send the issued token as `Authorization: Bearer <token>` on protected requests.

- Product reads are public; product changes require Admin.
- Listing all users requires Admin.
- Customers access their own user record and orders; Admin can access all.
- Registration grants Customer membership; it does not grant Admin.

See [Section 6](docs/part-2/section-06-authentication.md) for the complete access matrix and response codes.

## Documentation

### Part 1

- [Section 1 — Foundation and Products API](docs/part-1/section-01-foundation.md)
- [Section 2 — Product Operations and Tests](docs/part-1/section-02-product-operations.md)
- [Section 3 — Users, Orders, and OrderItems](docs/part-1/section-03-users-and-orders.md)
- [Section 4 — Spring Boot Inventory Service](docs/part-1/section-04-inventory-service.md)
- [Section 5 — CI and Terraform Fundamentals](docs/part-1/section-05-ci-and-terraform.md)

### Part 2

- [Section 6 — Authentication, Roles, and Order Ownership](docs/part-2/section-06-authentication.md)
- [Section 7 — Validation, Error Handling, and Structured Logging](docs/part-2/section-07-validation-errors-and-logging.md)

## Testing

Run all .NET tests:

```powershell
docker compose run --rm tools sh -c 'dotnet restore OrderManagementSystem.sln --force && dotnet test OrderManagementSystem.sln --no-restore'
```

From `services/inventory-service`, run Java tests:

```powershell
.\mvnw.cmd clean verify
```

The API integration suite has **11 passing tests**, verified locally and in GitHub Actions. Each test uses an isolated in-memory SQLite database, which closes with its factory. PostgreSQL-specific migrations and provider behavior require separate verification.

## Repository Structure

```text
.github/workflows/     Build/test workflows
src/                   .NET application projects
tests/                 .NET test projects
services/              Spring Boot Inventory Service
infra/learning/        Local Terraform exercise
docs/part-1/           Sections 1–5
docs/part-2/           Section 6
compose.yaml           Application and utility services
README.md              Project home
```

## Current Limitations

- Inventory uses sample data and does not integrate with Orders or persist to a database.
- Authentication uses local accounts and JWTs; SSO and refresh tokens are not implemented.
- AWS deployment is planned. The Terraform exercise creates no AWS resources.
- CI currently builds and tests; deployment automation is future work.
- Earlier section instructions are milestone records rather than a replacement for current configuration.

