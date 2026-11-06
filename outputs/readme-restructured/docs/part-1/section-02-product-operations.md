# Section 2 — Product Operations and Tests

[← Project home](../../README.md)

> This page records the implementation at this milestone. For current startup instructions and access rules, see the project home page and Section 6. Historical image tags and sample data are preserved.

### Completed Work ###

- Added product updates through the controller, service, and repository layers.
- Added product deletion through the same layers.
- Verified successful updates and deletion using Postman.
- Verified invalid update requests return validation errors.
- Verified missing products return `404 Not Found`.
- Added Docker Compose configuration for local startup.
- Moved local database credentials into an excluded `.env.local` file.
- Created the `OrderManagement.Application.Tests` xUnit project.
- Added four passing ProductService unit test cases.

### Local Architecture ###

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
### Local Configuration ###

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

### Docker Compose Configuration ###

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

### Start the Application ###

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

### Product Endpoints ###

| Method | Route | Successful response | Other expected responses |
|---|---|---|---|
| GET | `/api/products` | `200 OK` | — |
| GET | `/api/products/{id}` | `200 OK` | `404 Not Found` |
| POST | `/api/products` | `201 Created` | `400 Bad Request` |
| PUT | `/api/products/{id}` | `200 OK` | `400 Bad Request`, `404 Not Found` |
| DELETE | `/api/products/{id}` | `204 No Content` | `404 Not Found` |

### Update a Product ###

Use Postman to send:

```http
PUT http://localhost:5080/api/products/{id}
Content-Type: application/json
```

Replace `{id}` with an existing product ID.

Example JSON body:

```json
{
  "name": "Section 2 Updated Product",
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

### Delete a Product ###

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

### Postman Organization ###

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

### Manual Verification Results ###

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

### Automated Unit Tests ###

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

#### Run Tests in Visual Studio ####

1. Open the solution.
2. Select **Test > Test Explorer**.
3. Click **Run All Tests**.
4. Confirm all four test cases pass.

---

[Previous section](../part-1/section-01-foundation.md) | [Project home](../../README.md) | [Next section](../part-1/section-03-users-and-orders.md)
