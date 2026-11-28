# Section 12.1 — Local .NET-to-Java Client Foundation

**Later milestone:** [Section 12.3](section-12-3-local-integrated-orders.md) now connects this foundation to the local order workflow and records the applied local migrations. Statements below about pending integration describe the end of this earlier stage.

## Status — 27 November 2026

Client foundation implemented and verified. This is the first stage of integration, not completion of the order workflow. The .NET API still creates orders using its existing database-only flow; it does not yet reserve inventory during POST orders. AWS remains running and unchanged.

Section 11.2 manual security checks were confirmed by the user before this milestone.

## 1. Responsibilities and contract

The Application project defines IInventoryClient and reservation DTOs without depending on HTTP or JWT libraries. Infrastructure implements the HTTP client and development token provider. API startup registers the client only when Inventory:Enabled is explicitly true.

| Client method | Java operation |
|---|---|
| ReserveAsync | POST /api/inventory/reservations with a stable caller-supplied UUID and items |
| GetReservationAsync | GET /api/inventory/reservations/{id}; 404 means absent at the time of that read |
| ReleaseAsync | POST /api/inventory/reservations/{id}/release |

The client validates IDs/items, uses the dedicated Inventory audience/issuer, sends only inventory.reserve scope, and adds a correlation header. It never forwards an end-user JWT as the service credential.

IHttpClientFactory manages handlers. Redirects are disabled so credentials are not automatically followed to another address. Calls have a configurable 1–30 second deadline (default 5), bounded response buffering, and cancellation support. Non-success bodies are not copied into public exceptions or logged as credentials.

No automatic POST retries are configured. A timeout, network error, 5xx, or malformed successful mutation response is classified as an uncertain outcome, not proof that stock was unchanged. The coordinator must inspect/retry the same reservation identity, never invent a new UUID for the same logical attempt.

Explicit 400/401/403/404/409 rejections retain their HTTP status in a typed exception. Missing reservation lookup returns null. Caller cancellation remains cancellation; if dispatch already occurred, it still requires later reconciliation by the eventual workflow.

## 2. Local service identity

The development token provider reads the excluded local RSA private key and signs a short-lived RS256 token with subject order-management-api, audience Inventory.API, issuer Inventory.LocalIssuer, and scope inventory.reserve. It cannot request a stock-admin scope through this adapter.

This local issuer adapter is permitted only in Development. Enabling it in Production fails configuration. There is no new public login/token endpoint. An enterprise identity provider/client-credentials adapter will replace local signing before production-style AWS integration; the Order API must not carry a shared local test issuer private key into AWS.

Current local test keys can issue multiple scopes via the developer helper. Possession of that private key is therefore powerful even though this client emits only the reservation scope. The read-only mount is a local convenience, not a substitute for a production identity provider.

## 3. Optional Compose configuration

Default Compose behavior is unchanged. The optional compose.integration.yaml mounts only the excluded private key read-only, sets the Docker service URL http://inventory:8080/, and enables the client foundation. The API must retain Development for this local profile and its existing local database/user-JWT settings.

```powershell
docker compose -f compose.yaml -f compose.integration.yaml up -d --build api inventory
```

Do not use localhost:5081 for service-to-service calls from inside the API container: localhost there is the API container itself. Java's public verification key must match the mounted signing key. Generate/retain the local key using Section 11.2 before using this override.

Registering this client does not by itself connect order creation. There are no user-visible order behavior changes in this foundation stage.

## 4. Run the real client probe

1. Start the local secured Inventory container with operations enabled as described in Sections 11.1 and 11.2.
2. Ensure product 1 exists and has at least one available unit.
3. Keep .local-inventory-auth/private-key.pem private and excluded. Do not paste it into application settings or commit it.
4. From the repository root run:

```powershell
dotnet run --project tools/InventoryClientProbe/InventoryClientProbe.csproj --configuration Release -- --live
```

5. The probe records its reservation UUID in excluded .local-inventory-auth/client-probe-state.json BEFORE the network operation.
6. It calls Java through the actual .NET client with a freshly signed service token, reserves one unit, replays the same request, releases it, and replays the release.
7. Successful output says RESERVED then RELEASED; stock quantity is restored, while its version can increase.
8. If interrupted or failed, retain the state file. Rerun to reconcile the same ID. Do not assume a timeout means failure and create a second reservation.
9. If the prior probe is already RELEASED, rerunning confirms that state without a new deduction. It deliberately reuses its recorded ID.

The console probe is a development tool, not an API endpoint or production recovery worker. It prints no tokens/private keys and uses no customer password.

## 5. Verified results

- All 66 .NET tests passed (13 Application and 53 API/client tests).
- Client tests covered signed token scope/audience, request identity, authorization headers, correlation, explicit rejections, cancellation, timeout uncertainty, no automatic mutation retries, missing lookup, release, and malformed responses.
- A live .NET client called the secured local Java container: reserve/replay RESERVED, release/replay RELEASED. The unit was restored.
- No order/database schema changes or AWS changes were made by this stage.

## 6. Durable order workflow design — required next

Do not add reserve/save/release directly around SaveChanges without tracking and recovery. A database timeout can happen after commit; releasing stock blindly could leave an existing order without reserved inventory.

The next stage must implement:

1. A stable submission/idempotency identity bound to the authenticated user and a canonical payload fingerprint. Reuse with a different owner or payload must conflict.
2. A persisted submission record and reservation UUID before any Inventory mutation. Persist immutable price/item snapshots so retries do not silently change the order.
3. A database uniqueness constraint linking at most one order to a submission/reservation.
4. Explicit pending/completed/compensation states and a lease or concurrency mechanism so two workers cannot independently complete or release the same attempt.
5. Order persistence and submission completion atomically in the Order database. Keep external HTTP calls outside long database transactions.
6. Reconciliation of ambiguous reserve responses using the same UUID. A single absent read does not prove an in-flight request will never commit.
7. Durable compensation after a confirmed failure. On an ambiguous order commit, first determine whether the order exists before releasing anything.
8. Restart recovery for pending work, including failed releases; a catch block alone is not sufficient.
9. Defined API responses for pending/rejected operations, owner-scoped status lookup, and tests for concurrent same-key calls, crashes, uncertain commits, and recovery.
10. Align the existing .NET 100-item maximum with Inventory's 10-item limit deliberately before enabling integrated orders.

This design work is recorded, not claimed implemented. The project remains on the agreed progression: safe local integration first, then private Java AWS deployment and production identity-provider acquisition.

## Sources

- [Microsoft: IHttpClientFactory](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory)
- [Microsoft: HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)

[Back to README](../../README.md)
