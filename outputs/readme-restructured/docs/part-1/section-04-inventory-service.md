# Section 4 — Spring Boot Inventory Service

[← Project home](../../README.md)

> This page records the implementation at this milestone. For current startup instructions and access rules, see the project home page and Section 6. Historical image tags and sample data are preserved.

### Completed Work ###

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

### Technology Stack ###

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

### Project Location ###

```text
services/inventory-service
```

The Java project is managed through Maven and IntelliJ.
The existing .NET projects remain managed through the solution file.

### Local Architecture ###

```text
Postman
   |
   +--> localhost:5080 --> .NET Order API --> PostgreSQL
   |
   +--> localhost:5081 --> Spring Inventory Service --> Sample map
```

The services run independently. The .NET API does not call the Inventory
Service yet.

### Inventory Application Structure ###

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

### Object-Oriented Design ###

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

### Sample Inventory ###

| Product ID | Available quantity |
|---|---:|
| 1 | 100 |
| 2 | 50 |
| 3 | 0 |

These fixtures are independent of the .NET Products table.

A product with zero stock exists and returns a successful lookup.
An unknown product returns a not-found response.

### Inventory Endpoint ###

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

### Error Handling ###

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

### Run Tests Locally ###

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

### Docker Build ###

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

### Start Inventory Through Compose ###

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

### Manual Verification ###

The following behaviors were verified through Postman:

- Known product returns the expected inventory.
- Zero-stock product returns quantity 0.
- Unknown product returns 404.
- Zero, negative, and nonnumeric IDs return 400.
- Lookup still works after container restart.
- The .NET Products endpoint remains available on port 5080.

Restarting reconstructs the sample map. This is not database persistence.

### Terraform ###

Reviewed:

- Providers and platform API communication.
- Resources and desired infrastructure.
- Variables and environment-specific inputs.
- Outputs for people and automation.
- State and managed-resource identity.
- Initialization, validation, planning, and application.

---

[Previous section](../part-1/section-03-users-and-orders.md) | [Project home](../../README.md) | [Next section](../part-1/section-05-ci-and-terraform.md)
