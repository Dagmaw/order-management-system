# Order Management System

A layered .NET Order Management API and a separate Spring Boot Inventory Service, both backed by PostgreSQL. The project includes Docker Compose, automated tests, GitHub Actions, and a Terraform-based AWS deployment exercise.

## Current Features

- Products: creation, listing, retrieval, updates, and deletion with validation and conflict handling.
- Users: business user records linked to separate Identity login accounts.
- Orders: server-calculated totals, purchase-time price snapshots, ownership checks, durable submissions, idempotency, and recovery.
- Authentication: Customer registration, JWT login, and Customer/Admin permissions.
- Inventory: PostgreSQL-backed lookup plus transactional stock reservation/release, version checks, scoped JWT security, and Flyway migrations.
- Automated .NET service/API tests and Java service/endpoint tests.
- Product request validation with FluentValidation.
- Centralized Problem Details exception responses.
- Serilog JSON logging with request correlation IDs.
- AWS infrastructure as code: private RDS, VPC routing, ECR, ECS Fargate API/setup tasks, ALB, ACM HTTPS, Route 53, Secrets Manager, and CloudWatch logs.
- GitHub OIDC authentication, image publishing, approved infrastructure changes, secret initialization, database migrations, and automatic read-only deployment verification.
- Separate approved database setup operations for migrations, application-login configuration, and Admin assignment.

## Project Status

| Area | Current state |
|---|---|
| Local Order API | PostgreSQL persistence, Identity/JWT, validation, and structured logging implemented |
| Local Inventory Service | PostgreSQL persistence with JPA/Hibernate and Flyway implemented |
| Service integration | Local and AWS Orders-to-Inventory integration verified; private HTTPS, Cognito machine tokens, durable recovery and idempotent submissions |
| CI | .NET, Java, isolated integration/recovery, and private stock-verifier checks passing |
| AWS exercise | Rebuilt and deployed; migrations, application login, and controlled Admin assignment verified |
| Public AWS API | HTTPS API on private Fargate behind ALB; public functional and task-replacement persistence checks passed |

Deployment and verification completed on 27 November 2026. Exercise resources remain running. See [Section 9.2](docs/part-2/section-09-2-aws-deployment.md) for results, limitations, and cleanup planning.

## Technology Stack

| Area | Technologies |
|---|---|
| Order API | C#, .NET 10, ASP.NET Core, Entity Framework Core 10 |
| Authentication | ASP.NET Core Identity, JWT Bearer authentication |
| Application database | PostgreSQL with Npgsql |
| Inventory | Java 25, Spring Boot 4.1.2, Spring Data JPA, Hibernate, Flyway, Maven Wrapper |
| Testing | xUnit, Moq, WebApplicationFactory, in-memory SQLite, JUnit, Spring Boot Test, H2 |
| Containers and CI | Docker, Docker Compose, GitHub Actions |
| AWS infrastructure | Terraform, S3 state/locking, ECR, VPC, private RDS, ECS Fargate, Secrets Manager, CloudWatch |
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
                           → Spring Data JPA / Hibernate
                           → Inventory PostgreSQL on Windows host
```

- **Domain:** business entities and relationships.
- **Application:** DTOs, service workflows, and contracts.
- **Infrastructure:** database access, migrations, and Identity implementation.
- **API:** endpoints, authentication, authorization, and dependency registration.

The optional local integration profile uses local service tokens. In AWS, Orders uses Cognito client credentials to reserve stock over private HTTPS. Durable submission tracking and idempotent reservation/release support recovery across service boundaries. See Sections 12.3 and 13.6-13.9.

AWS uses separate logical Orders and Inventory databases/logins on the same private RDS instance, private Fargate services, a public API ALB and a private Inventory ALB, plus approved one-off setup/verification tasks. See the [current architecture](docs/part-5/architecture.md), [operations runbook](docs/part-5/operations-runbook.md), and [repeatable demo](docs/part-5/demo.md). Section 9.2 records the earlier deployment history.

## Quick Start

Run commands from the repository root. Docker Desktop must be running Linux containers. PostgreSQL runs separately on the Windows host; provision the `ordermanagement` and `inventory` databases and their local logins using [Section 1](docs/part-1/section-01-foundation.md) and [Section 8](docs/part-2/section-08-inventory-persistence.md).

1. Create your excluded `.env.local` beside `compose.yaml`. Supply your own database credentials and JWT configuration:

   ```dotenv
   ASPNETCORE_ENVIRONMENT=Development
   DOTNET_ENVIRONMENT=Development
   ConnectionStrings__DefaultConnection='Host=host.docker.internal;Port=5432;Database=ordermanagement;Username=ordermanagement_app;Password=YOUR_DATABASE_PASSWORD'
   Jwt__Issuer=OrderManagement.API
   Jwt__Audience=OrderManagement.Client
   Jwt__AccessTokenMinutes=15
   Jwt__SigningKeyBase64=YOUR_BASE64_SIGNING_KEY
   BootstrapAdmin__Enabled=false
   ```

   Replace both placeholders before startup. This illustrates a simple password; quote/escape connection-string values correctly if your password contains special characters. The signing key must decode to at least 32 random bytes. Keep credentials and tokens out of Git. See [authentication configuration](docs/part-2/section-06-authentication.md).

2. Create the excluded `.env.inventory.local` beside `compose.yaml`:

   ```dotenv
   SPRING_DATASOURCE_URL=jdbc:postgresql://host.docker.internal:5432/inventory
   SPRING_DATASOURCE_USERNAME=inventory_app
   SPRING_DATASOURCE_PASSWORD='YOUR_LOCAL_INVENTORY_PASSWORD'
   ```

   Replace the password. Flyway creates/version-controls the Inventory schema at startup; Hibernate validates it. Follow [Section 8](docs/part-2/section-08-inventory-persistence.md) for database ownership and permissions.

3. Apply committed EF Core migrations to a new or outdated Order API database. Use a login with schema-migration permissions for this operation; the runtime account may have fewer privileges:

   ```powershell
   docker compose run --rm tools sh -c 'dotnet tool restore --tool-manifest dotnet-tools.json && dotnet restore OrderManagementSystem.sln --force && dotnet ef database update --project src/OrderManagement.Infrastructure --startup-project src/OrderManagement.API --context ApplicationDbContext'
   ```

4. Build and start both services:

   ```powershell
   docker compose up -d --build api inventory
   docker compose ps
   docker compose logs --tail 80 api inventory
   ```

| Service | Local address |
|---|---|
| Order Management API | http://localhost:5080 |
| Inventory Service | http://localhost:5081 |

PostgreSQL runs separately on the Windows host. Inside Docker, `host.docker.internal` points to that host.

5. Check a public product endpoint and `GET http://localhost:5081/api/inventory/1`. Inventory seed data is managed by Flyway; a product with zero stock still returns a successful lookup. See [Section 8](docs/part-2/section-08-inventory-persistence.md) for missing/invalid-ID scenarios and persistence checks.

To stop the local containers, run `docker compose down`. This does not delete the separately hosted PostgreSQL databases.

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
- [Section 8 — Inventory Persistence with PostgreSQL](docs/part-2/section-08-inventory-persistence.md)
- [Section 9.1 — AWS Deployment and Verification](docs/part-2/section-09-1-aws-deployment.md)
- [Section 9.2 — Detailed AWS Deployment Walkthrough](docs/part-2/section-09-2-aws-deployment.md)


### Part 3

- [Section 10.1 — Local OpenAPI and Swagger UI](docs/part-3/section-10-1-openapi-swagger.md)
- [Section 10.2 — OpenAPI and Swagger UI on AWS](docs/part-3/section-10-2-openapi-swagger-aws.md)

- [Section 11.1 — Local Inventory Reservations and Release](docs/part-3/section-11-1-inventory-reservations.md)

- [Section 11.2 — Local Inventory JWT Security](docs/part-3/section-11-2-inventory-security.md)

- [Section 12.1 — Local .NET-to-Java Client Foundation](docs/part-3/section-12-1-local-inventory-client.md)

- [Section 12.2 — Durable Order Submission Tracking](docs/part-3/section-12-2-durable-submission-tracking.md)

- [Section 12.3 — Local Integrated Orders and Recovery](docs/part-3/section-12-3-local-integrated-orders.md)
- [Section 12.4 — Automated Integration and Recovery Tests](docs/part-3/section-12-4-integration-recovery-tests.md)
- [Section 12.5 — Orders Service Identity Adapter](docs/part-3/section-12-5-service-identity-adapter.md)
- [Section 12.6 — Inventory Provider JWT Validation](docs/part-3/section-12-6-inventory-provider-jwt.md)
- [Section 12.7 — Amazon Cognito Service Identity](docs/part-3/section-12-7-cognito-service-identity.md)
- [Section 13.1 — Private Inventory Deployment Foundation](docs/part-4/section-13-1-private-inventory-foundation.md)
- [Section 13.2 - Inventory Database Bootstrap](docs/part-4/section-13-2-inventory-database-bootstrap.md) - completed in AWS; dedicated database/login verified.
- [Section 13.3 - Private Inventory Service Activation](docs/part-4/section-13-3-private-inventory-activation.md) - running privately in AWS; deployment observation permission corrected.
- [Section 13.4 - Orders Database Prerequisite](docs/part-4/section-13-4-orders-database-prerequisite.md) - migrations completed in AWS; subsequent API integration deployed.
- [Section 13.5 - Orders Inventory Release Preparation](docs/part-4/section-13-5-orders-inventory-release.md) - preparation merged; trusted API candidate published.
- [Section 13.6 - Activate Orders to Private Inventory](docs/part-4/section-13-6-activate-orders-inventory.md) - deployed successfully; both services healthy.
- [Section 13.7 - Live AWS Orders Smoke Checks](docs/part-4/section-13-7-live-aws-orders-smoke.md) - public checks verified; follow-up private stock check also passed.
- [Section 13.8 - Private Stock Verifier](docs/part-4/section-13-8-private-stock-verifier.md) - 22 isolated checks passed; verifier image published.
- [Section 13.9 - Private Verification Task](docs/part-4/section-13-9-private-verification-task.md) - deployed; real AWS stock/replay/conflict/rejection verification passed.

### Part 5

- [Section 14 - Deployment Automation](docs/part-5/section-14-deployment-automation.md) - approved workflow demonstration passed all 17 read-only checks.

## Testing

Run all .NET tests:

```powershell
docker compose run --rm tools sh -c 'dotnet restore OrderManagementSystem.sln --force && dotnet test OrderManagementSystem.sln --no-restore'
```

From `services/inventory-service`, run Java tests:

```powershell
.\mvnw.cmd clean verify
```

The .NET tests cover service behavior, authentication, registration/login, role authorization, order ownership, validation, and error handling. API integration tests use in-memory SQLite; Java tests use the test configuration, including H2. These do not replace PostgreSQL migration and persistence verification. Test counts and outcomes should be read from the latest run rather than an older milestone count.

## Automation

| Workflow | Purpose |
|---|---|
| [Build and Test](.github/workflows/ci.yml) | .NET/Java checks; API image publishing after successful main-branch push checks |
| [Terraform Lock](.github/workflows/terraform-lock.yml) | Provider lock-file maintenance |
| [Terraform AWS Dev](.github/workflows/terraform-dev.yml) | Formatting, validation, planning, and approved saved-plan application |
| [Publish Database Setup Image](.github/workflows/publish-database-setup.yml) | Build/publish the setup image and report its immutable ECS reference |
| [Initialize Secret](.github/workflows/initialize-secret.yml) | Initialize application database credentials while preserving an existing current value |
| [Run Database Setup](.github/workflows/run-database-setup.yml) | Run migrations or controlled Admin assignment in a private Fargate task and verify its exit code |

Publishing an image does not deploy a running API service. AWS publishing jobs require the project repository and IAM roles to exist; they can fail after cleanup until prerequisites are restored or publishing is disabled.

## Repository Structure

```text
.github/workflows/     CI, image publishing, Terraform, secrets, migrations
src/                   .NET application projects
tests/                 .NET test projects
services/              Spring Boot Inventory Service with JPA/Flyway
infra/learning/        Local Terraform exercise
infra/aws/dev/         AWS deployment infrastructure
docs/part-1/           Sections 1–5
docs/part-2/           Sections 6–9
compose.yaml           Application and utility services
src/OrderManagement.DatabaseSetup/  AWS migration/application-login program
README.md              Project home
```

## Current Limitations

- Inventory integration is verified locally through Section 12.3, and real AWS Cognito service tokens passed all 20 integration/recovery checks in Section 12.7. Java AWS deployment and integrated production configuration remain pending. Initial inventory rows are Flyway seed data.
- Authentication uses local accounts and JWTs; SSO and refresh tokens are not implemented.
- AWS functional verification passed; production load, disaster recovery, and high availability testing remain outside this exercise. See Section 9.2.
- AWS exercise resources remain running; review costs and the exact plan before any cleanup. Preserve the company domain and hosted zone.
- Earlier section instructions are milestone records rather than a replacement for current configuration.

## Operational handoff

- [Section 15 - Handoff and Evidence](docs/part-5/section-15-operational-handoff.md)
- [Current runtime and deployment diagrams](docs/part-5/architecture.md)
- [Operations, incident triage, and recovery runbook](docs/part-5/operations-runbook.md)
- [Read-only demo and controlled order-verification option](docs/part-5/demo.md)

AWS resources remain running for the project. Historical section instructions describe their milestone; the current Terraform configuration and the handoff evidence describe the deployed system.

- [Section 16 - Private Inventory Swagger tunnel](docs/part-5/section-16-inventory-session-tunnel.md)

## Access Inventory Swagger from Windows

Inventory is private. Use Docker Desktop with Linux containers and the tunnel script to open a dedicated Chrome window:

```powershell
cd D:\Docs\30dayPlan\order-management-system
.\scripts\Start-InventorySwaggerTunnel.ps1
```

If PowerShell reports that running scripts is disabled, allow local scripts for the current terminal session, then retry:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned
.\scripts\Start-InventorySwaggerTunnel.ps1
```

The process setting ends when that terminal closes. Organization-managed Group Policy may override it; use `Get-ExecutionPolicy -List` to inspect the effective scopes.

Use the Chrome window opened by the script for [Inventory Swagger](https://inventory.dev.internal.kaddigitalsolutions.com/swagger-ui/index.html). Ordinary browser windows cannot resolve the private hostname. The script verifies the HTTPS certificate before opening Chrome.

The current script supports Windows PowerShell 5.1: container ownership is checked using Docker JSON instead of a quoted Go template. If an older copy reports `function "oms" not defined`, update to the corrected script.

When finished:

```powershell
.\scripts\Stop-InventorySwaggerTunnel.ps1
```

This stops the local tunnel and its EC2 helper. API, Inventory, database and existing network resources stay running. A stopped helper still retains its small billed EBS volume.

For the instance ID/settings file, role permissions, cost estimate and troubleshooting, see [Section 16](docs/part-5/section-16-inventory-session-tunnel.md).
