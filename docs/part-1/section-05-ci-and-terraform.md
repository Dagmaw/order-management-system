# Section 5 — CI and Terraform Fundamentals

[← Project home](../../README.md)

> This page records the implementation at this milestone. For current startup instructions and access rules, see the project home page and Section 6. Historical image tags and sample data are preserved.

### Completed Work ###

- Ran the .NET automated tests successfully.
- Ran the Spring Boot automated tests successfully.
- Verified API behavior using Postman.
- Verified Docker Compose startup and container restarts.
- Confirmed PostgreSQL-backed application data persists across API restarts.
- Added GitHub Actions workflows for .NET and Java builds and tests.
- Confirmed both CI jobs passed on GitHub.
- Practiced the Terraform workflow locally through Docker Compose.

### Local Services ###

| Service | Local URL | Storage |
|---|---|---|
| Order Management API | http://localhost:5080 | PostgreSQL |
| Inventory Service | http://localhost:5081 | In-memory sample data |

PostgreSQL runs on the Windows host. The API container connects using
the connection settings supplied through `.env.local`.

The Inventory Service currently uses sample inventory data.
It does not yet persist inventory to a database or integrate with Orders.

### API Verification ###

| Area | Scenario | Expected result |
|---|---|---|
| Products | Create a valid product | 201 Created |
| Products | List products | 200 OK |
| Products | Retrieve an existing product | 200 OK |
| Products | Update an existing product | 200 OK |
| Products | Submit invalid product input | 400 Bad Request |
| Products | Update a missing product | 404 Not Found |
| Products | Delete an unreferenced product | 204 No Content |
| Products | Retrieve a deleted product | 404 Not Found |
| Products | Delete a product referenced by an order | 409 Conflict |
| Users | Create a valid user | 201 Created |
| Users | Retrieve an existing user | 200 OK |
| Users | Create a user with a duplicate email | 409 Conflict |
| Orders | Create a valid order | 201 Created |
| Orders | List or retrieve orders | 200 OK |
| Orders | Submit invalid quantities or references | Request rejected |
| Inventory | Retrieve inventory for product 1 | 200 OK; quantity 100 |
| Inventory | Request a missing inventory item | 404 Not Found |
| Inventory | Request an invalid product ID | 400 Bad Request |

Order prices and totals are calculated on the server.
Order items retain the product price recorded when the order was created.

### Start the Application Services ###

Run from the repository root:

```powershell
docker compose up -d --build api inventory
docker compose ps
```

Local PostgreSQL must be available, and `.env.local` must contain
the required API configuration before startup.

### Continuous Integration ###

Workflow file:

```text
.github/workflows/ci.yml
```

The workflow runs on:

- Pushes to `main`.
- Pull requests targeting `main`.
- Manual execution through GitHub Actions.

| Job | Responsibilities |
|---|---|
| .NET — Build and Test | Restore dependencies, build, and run .NET tests |
| Java — Build and Test | Use Java 25 and the Maven Wrapper to build and run tests |

Both jobs passed during Part 1 verification.

This workflow builds and tests the applications.
Deployment automation is planned for later work.

### Terraform Learning Exercise ###

Configuration location:

```text
infra/learning/main.tf
```

Terraform runs through the Compose `terraform` utility service.

The exercise uses the built-in `terraform_data` resource to store
an example Inventory Service configuration in local state.

No AWS resources are created by this exercise.

Commands, run from the repository root:

```powershell
docker compose run --rm terraform init
docker compose run --rm terraform fmt
docker compose run --rm terraform validate
docker compose run --rm terraform plan "-out=learning.tfplan"
docker compose run --rm terraform apply learning.tfplan
docker compose run --rm terraform output
docker compose run --rm terraform state list
docker compose run --rm terraform plan
```

Verified learning outcomes:

- Created `terraform_data.inventory`.
- Inspected its saved output and resource address.
- Changed the example replica value from 1 to 2.
- Reviewed and applied the update.
- Confirmed a final plan reported no changes.

The replica value is learning data stored in Terraform state.
It does not scale the running Inventory Service.

### Configuration and Git Hygiene ###

- `.env.local` is excluded from Git.
- Terraform state, saved plans, and working directories are excluded from Git.
- Application source, tests, Dockerfiles, Compose configuration,
  CI workflow, and Terraform configuration are version controlled.
- Credentials must not be included in README examples or committed files.

---

[Previous section](../part-1/section-04-inventory-service.md) | [Project home](../../README.md) | [Next section](../part-2/section-06-authentication.md)
