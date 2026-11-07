# Section 7 — Validation, Error Handling, and Structured Logging

[← Project home](../../README.md)

## Goal

Improve request validation, centralize exception handling, and make
requests easier to troubleshoot through structured logs and correlation IDs.

## Implemented

- Added FluentValidation validators for product creation and updates.
- Registered validators through dependency injection.
- Called validators explicitly from product controller actions.
- Added centralized exception handling using IExceptionHandler.
- Removed repeated exception-handling blocks from controllers.
- Configured Serilog to write structured JSON logs to the console.
- Added HTTP request-completion logging.
- Added correlation IDs to response headers and request logs.
- Added automated validation and error-handling tests.

## Request Validation

Product creation and update validators enforce:

| Field | Rule |
|---|---|
| Name | Required; maximum 200 characters |
| Description | Maximum 200 characters |
| Price | Between 0.01 and 9999999999999999.99, inclusive |

Existing Data Annotations remain during this transition. They may reject
invalid requests before the controller invokes FluentValidation.

Business checks remain in the service layer, including user/product
existence, order quantities, duplicate products, and total limits.

## Centralized Exception Handling

GlobalExceptionHandler translates application exceptions into HTTP
Problem Details responses.

| Exception | Status |
|---|---|
| DuplicateEmailException | 409 Conflict |
| RegistrationValidationException | 400 Bad Request |
| OrderValidationException | 400 Bad Request |
| DataConflictException | 409 Conflict |
| Unexpected exception | 500 Internal Server Error |

Unexpected errors return a generic public message. Exception details
are recorded in server logs.

Normal controller responses and authentication/authorization failures
do not need to throw exceptions to produce their status codes.

## Structured Logging

Serilog captures application logs through ILogger and writes JSON events
to standard output.

Request-completion events include:

- HTTP method
- Request path
- Response status
- Elapsed time
- Application name
- Correlation ID

Request bodies, passwords, and Bearer tokens are not intentionally logged
by the request-logging configuration.

Inspect logs from the repository root:

```powershell
docker compose logs --tail 100 api
```

Follow logs:

```powershell
docker compose logs -f api
```

## Correlation IDs

CorrelationIdMiddleware reuses the current activity identifier, with
HttpContext.TraceIdentifier as a fallback.

| Location | Property |
|---|---|
| Response header | X-Correlation-ID |
| Structured logs | CorrelationId |
| Centrally handled exception body | correlationId and traceId |

For centrally handled exceptions, the header and both response-body
identifiers match.

Other response bodies, including framework validation responses, are
not automatically given the same custom fields by this middleware.

## Automated Tests

Added 16 product-validator cases covering:

- Blank and whitespace-only names
- Invalid prices
- Overlong text
- Accepted text lengths and price boundaries

Added 7 API error-handling and correlation cases covering:

- Four known application exception mappings
- Safe handling of unexpected exceptions
- Correlation headers on successful requests
- Correlation headers on unauthorized requests

The error-mapping tests replace IAuthService inside the test host to
produce deterministic exceptions. They test the real HTTP pipeline and
exception handler without deliberately breaking the database.

The API integration suite contains 18 cases, including the original
authentication, authorization, and ownership tests.

Tests continue to use isolated in-memory SQLite databases.

## Run Tests

From the repository root:

```powershell
docker compose run --rm tools sh -c 'dotnet restore OrderManagementSystem.sln --force && dotnet test OrderManagementSystem.sln --no-restore'
```

## Docker Verification

Rebuild and start the API:

```powershell
docker compose up -d --build api
docker compose ps api
```

Postman verification scenarios:

| Scenario | Expected |
|---|---|
| Invalid product creation | 400 with field errors |
| Invalid product update | 400; existing values unchanged |
| Duplicate registration | 409 with matching correlation identifiers |
| Order referencing a missing product | 400 |
| Deletion of a referenced product | 409; product remains |
| Successful product listing | 200 with correlation header |

Match an error response's X-Correlation-ID to CorrelationId in Docker logs.

## Limitations

- Product DTO Data Annotations and FluentValidation rules coexist.
- FluentValidation currently covers product creation and updates.
- SQLite tests do not verify PostgreSQL-specific provider behavior.
- Unexpected exception details in server logs require appropriate
  operational access controls.
- Central exception handling does not roll back database changes;
  transactions and database constraints maintain data consistency.

---

[← Section 6](section-06-authentication.md) |
[Project home](../../README.md)