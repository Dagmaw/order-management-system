# Section 11.1 — Local Inventory Stock, Reservations, and Release

Section 11.2 now protects these operations with JWT scopes. The unauthenticated walkthrough below records Section 11.1; when running the current application, first follow [Section 11.2](section-11-2-inventory-security.md) and authorize with the appropriate local token.

## Scope

Java owns inventory and reservation data. This milestone implements stock changes, atomic multi-product reservations, idempotent reservation/release behavior, and database concurrency protection. It does not yet connect Orders to Inventory or add service authentication. AWS remains unchanged and running.

Stock-changing routes are disabled by default. Enable them only for local development until Section 11.2 adds security. Docker Compose already binds the service to 127.0.0.1. Do not expose these unauthenticated operations publicly.

## Contract

| Method and path | Behavior |
|---|---|
| GET `/api/inventory/{productId}` | Current available quantity and stock version |
| PUT `/api/inventory/{productId}/stock` | Set available quantity only when expectedVersion matches |
| POST `/api/inventory/reservations` | Atomically reserve 1–10 different products; returns 200 for creation and replay |
| GET `/api/inventory/reservations/{id}` | Read current state for retry reconciliation |
| POST `/api/inventory/reservations/{id}/release` | Restore reserved quantities exactly once; returns 200 |

The caller generates a UUID reservationId before sending a request. Reuse that ID and identical items for a retry. Reordered items are considered identical. Reusing it with different items returns 409. A released reservation cannot be reserved again under the same ID.

No distributed transaction with .NET exists here. A future order integration will need compensation and reconciliation. Reservations do not expire automatically or transition to a fulfilled state in this milestone.

## Database and transaction design

Flyway V2 adds a version column to existing inventory and creates reservation header/item tables with foreign keys, unique keys, and quantity/status constraints. Do not edit previously applied V1.

Each reserve/release runs in one database transaction. Stock rows are locked with SELECT FOR UPDATE in increasing product ID order. Any missing or insufficient item rolls back the entire reservation, including its header and previous deductions. Release locks the reservation before checking its status, preventing repeated restoration.

Concurrent requests with the same new UUID compete on a database primary key. If another transaction wins, the losing transaction is rolled back and retried once in a new transaction to read and validate the winner. PostgreSQL's aborted transaction is never reused.

Every stock-changing operation increments version. A stale administrative stock update returns 409 instead of overwriting a reservation's change. Updating available quantity means setting currently unreserved stock; existing reservations remain held and a later release restores them.

This implementation uses Spring-managed JDBC transactions for the new operations. JPA remains for existing lookup. It does not rely on an in-process lock, which would fail across multiple service instances.

## Start locally

1. Review and save the migration, service, controller, DTO, error handling, and test changes.
2. In excluded `.env.inventory.local`, retain all database settings and add:

```dotenv
API_DOCS_ENABLED=true
INVENTORY_OPERATIONS_ENABLED=true
```

3. Do not commit that file or credentials.
4. Rebuild with `docker compose up -d --build inventory` from the root.
5. Check `docker compose logs --tail 80 inventory` for successful Flyway V2 and startup. This migration adds tables/version metadata; it does not reset existing quantities.
6. Open `http://localhost:5081/swagger-ui/index.html`. Refresh so the document includes the newly enabled routes.

## Manual verification in Swagger UI

Use existing product 1 and record its initial availableQuantity and version. Do not assume stock is still 100; use the actual lookup response. Run only against the local Inventory database.

1. GET inventory/1. Record quantity Q and version V.
2. If stock is too low for a 2-unit test, PUT inventory/1/stock with a suitable nonnegative availableQuantity and the exact current expectedVersion. A stale version must return 409; refresh GET before trying again.
3. Generate a fresh UUID, for example with `[guid]::NewGuid()` in PowerShell, then POST reservations:

```json
{
  "reservationId": "REPLACE_WITH_A_FRESH_UUID",
  "items": [{ "productId": 1, "quantity": 2 }]
}
```

4. Require 200 and status RESERVED. GET inventory/1 should now show Q minus 2 and a newer version.
5. Execute the exact same POST again. Require 200 and no additional deduction.
6. Change quantity to 3 but keep that UUID. Require 409 and no stock change.
7. POST reservations/{id}/release using the original UUID and no body. Require RELEASED and quantity restored by 2.
8. Repeat release. Require RELEASED and no second restoration.
9. Try reserving the released UUID again. Require 409; use a new UUID for new work.
10. Test quantity 0, duplicate products, no items, and a malformed UUID. Require 400.
11. Try a new reservation containing more units than available. Require 409 and unchanged stock.
12. Try a new reservation with an existing product followed by a missing product. Require 404 and no partial deduction.

Stock update example (replace version with the latest GET result):

```json
{ "availableQuantity": 20, "expectedVersion": 0 }
```

Do not keep retrying a stale absolute update blindly. Read current state and decide whether the desired correction is still valid.

## Automated verification

Run `./mvnw.cmd --batch-mode --no-transfer-progress clean verify` from services/inventory-service using JDK 25.

The test suite covers rollback, conflicting duplicate payloads, repeated release, version conflicts, missing products, HTTP requests, and generated OpenAPI paths. Twenty competing one-unit reservations against ten units must produce exactly ten successes and zero remaining stock. Concurrent identical UUIDs deduct stock once; concurrent releases restore it once.

H2 tests initialize both migration scripts as schema fixtures; they do not validate Flyway's real PostgreSQL execution. Run the same ReservationServiceTests against an isolated PostgreSQL database as well, enabling Flyway, disabling SQL fixture initialization, and selecting the PostgreSQL driver. Never point these destructive test fixtures at local application or AWS databases: tests delete reservation records and replace fixture products.

## Verification record — 27 November 2026

- Java clean verify: 18 tests passed.
- Isolated PostgreSQL 18: all 8 ReservationServiceTests passed, using actual Flyway migrations V1/V2, with schema validation enabled.
- Application and AWS database records were not used by the destructive automated fixtures.

## Remaining work

- Section 11.2: Spring Security/JWT and permissions before any public deployment.
- .NET integration: reserve stock, create order, compensate failures, handle timeouts and uncertain outcomes.
- Decide reservation finalization/expiry and administrative stock-management policy during integration design.
- Automate PostgreSQL verification in CI and deploy Java privately to AWS at the agreed later milestone.

[Back to README](../../README.md)
