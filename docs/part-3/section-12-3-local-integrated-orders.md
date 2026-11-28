# Section 12.3 — Local Integrated Orders and Recovery

## Status — 27 November 2026

The local .NET Order API now reserves stock through the secured Java service when the optional integration profile is enabled. Durable submissions, idempotent retries, atomic order completion, compensation, and a recovery worker are implemented. Local migrations were applied and the integrated containers rebuilt. AWS remains running and unchanged; do not enable local token issuance in AWS.

## 1. Request and state flow

```text
Authenticated caller + Idempotency-Key
    → validate owner/items and freeze prices
    → persist Pending submission and stable reservation UUID
    → Java reserve with inventory.reserve service token
    → Reserved
    → one database transaction: insert Order + mark Completed

Definitive failure before completion
    → persist Compensating while confirming no linked Order exists
    → Java release/cancellation marker
    → Compensated

Network/commit uncertainty
    → retain durable state
    → recovery worker resumes with the SAME reservation identity
```

No database transaction is held open across an Inventory HTTP request. A conditional lease/state UPDATE serializes order completion against compensation. The order's unique nullable InventorySubmissionId prevents two orders for one attempt. Existing orders retain null in that column.

The generic submission store cannot mark Completed. The coordinator does so only in the same database transaction that inserts the linked order. An uncertain commit is never treated as proof that the order failed: persisted state is read on retry/recovery, and stock is not blindly released.

## 2. Important Java cancellation change

Release of a previously unknown reservation UUID now creates a durable RELEASED marker with no items and returns 200. It does not add stock. A delayed reserve using that UUID then conflicts instead of deducting stock after cancellation. Existing reservation release still restores its held quantities exactly once.

This extends Section 11.1's release semantics. A missing reservation GET still returns 404. A released marker may legitimately have an empty items list, which the .NET client now accepts for RELEASED responses.

## 3. Local configuration and migration

1. Keep the separate Order and Inventory local databases and JWT settings.
2. Retain the excluded Inventory RSA private key and matching Java public key from Section 11.2.
3. Apply the two additive EF migrations locally: AddDurableOrderSubmissions and LinkOrdersToInventorySubmissions. They add the tracking table, failure reason, order-link column, and unique index. They do not reset products, users, orders, or stock.
4. Design-time migration configuration reads OMS_MIGRATION_CONNECTION or ConnectionStrings__DefaultConnection. If the password is separate, supply PGPASSWORD privately as well. Do not put credentials in command history or source.
5. This milestone applied them to the existing localhost database using its excluded settings. Do not apply them to AWS merely because local testing passed; AWS deployment remains a separate reviewed milestone.
6. Start the local integrated services from the root:

```powershell
docker compose -f compose.yaml -f compose.integration.yaml up -d --build api inventory
```

7. Confirm startup logs and Swagger on ports 5080 and 5081. Inside Compose the Order client calls http://inventory:8080/, not host localhost:5081.
8. The local Order API remains Development for the local issuer adapter; the deployed AWS API remains Production. The default Compose file does not enable integration by itself.

Do not drop the database or edit an applied migration to solve a startup mismatch.

## 4. Place an integrated order manually

1. Open local Order Swagger: `http://localhost:5080/swagger/index.html`.
2. Log in with a local Customer account, copy its accessToken, and Authorize. This is the Order API's user token. The API internally creates a separate Inventory service token; do not paste an Inventory token into Order Swagger.
3. Read local Products and Inventory to find a product existing in both systems with enough stock. Product IDs are independently maintained at this milestone; catalog synchronization is not implemented.
4. Generate a new UUID with `[guid]::NewGuid()` for this logical order.
5. Expand POST /api/orders → Try it out. Enter that UUID in the Idempotency-Key header field and a body such as:

```json
{
  "items": [{ "productId": 1, "quantity": 1 }]
}
```

6. Customer requests omit userId; Admin requests must supply the intended user's numeric userId. Existing owner/role rules still apply before submission.
7. Expect 201 with an Order when processing completes immediately, or 202 with submission state and a status Location while processing/recovery is pending.
8. If 202, GET /api/orders/submissions/{id} using the same authenticated actor. The status response contains state, optional orderId, and a safe failure reason. Other actors cannot read that submission.
9. Completed means the linked order exists. Retrieve it through the existing owner-protected GET orders/{id} route.
10. Rejected/Compensated attempts return 409 on repeated POST; a 200 response from the status endpoint only means the status was retrieved, not that the order succeeded.

Integrated orders are limited to 10 unique products, matching Inventory, with each quantity between 1 and 1000. The legacy 100-item behavior remains only when integration is disabled.

## 5. Idempotency verification

1. Record the order ID, key, and stock after the first successful order.
2. Repeat the identical POST with the same key. Expect 201 with the SAME order ID and no extra stock deduction.
3. Change the quantity but retain the key. Expect 409; a request key cannot identify different order contents.
4. A genuinely different order needs a new UUID. Do not create a new key just because a request timed out.
5. Price snapshots come from the first accepted submission, not a later retry's catalog prices.
6. An absent Inventory lookup is not sufficient evidence to abandon an in-flight request; the coordinator uses stable IDs and cancellation fencing.

## 6. Outage and recovery verification

1. Keep the Order API running and use a fresh order key.
2. Temporarily stop only local Inventory: `docker compose stop inventory`.
3. Submit a valid order. Expect 202 with persisted Pending state rather than a completed order without stock.
4. Restart Inventory with `docker compose start inventory`.
5. The Order recovery worker scans eligible Pending/Reserved/Compensating records every 10 seconds in bounded batches. It uses a database lease before processing each attempt.
6. Poll the owner-scoped status until Completed or a terminal failure. Successful recovery creates one order and deducts stock once.
7. Repeating the same order key returns that same result. Do not reset or discard tracking records while work is pending.

Credentials/authorization failures and transient failures remain eligible for recovery after configuration is corrected. Persistent failure needs operational attention; this milestone logs progress failures but does not yet provide production alarms/backoff/dead-letter operations.

## 7. Confirmed verification results

- .NET suite: 76 passed (13 Application and 63 API/client/store/coordinator tests).
- Java suite: 22 passed after adding release-marker cancellation fencing.
- Coordinator tests cover same-key order replay, immutable price snapshots, changed payload conflict, cross-actor status denial, lost reserve response recovery, failed compensation retry, and a database commit whose acknowledgement is lost.
- The lost-commit test confirms an existing order's stock is NOT released after the acknowledgement failure.
- Live local HTTP: order 5 returned 201; replay returned order 5; changed quantity with same key returned 409; stock deducted once.
- Live outage: valid request returned 202 while Java was stopped. After restart, the worker completed order 6. The two test orders consumed exactly two units total.
- Verification account credentials and request identities are retained only in excluded .local-inventory-auth/integration-verification.json; no credentials appear in this guide.

The successful test orders intentionally remain in the local database with held stock. Reservations do not yet have a fulfilment/finalization or expiry workflow. Do not release stock for completed orders as an ad hoc cleanup action.

## 8. Remaining before AWS rollout

- Automate the full two-service outage/restart tests in CI; current live verification was executed locally.
- Expand real PostgreSQL fault-injection/concurrent coordinator testing and worker-restart tests beyond the current store concurrency and live outage checks.
- Add production-grade identity-provider token acquisition, operational metrics/alarms, and retry/backoff policy.
- Decide fulfilment, reservation retention, and catalog ID synchronization policies.
- Deploy Java privately in AWS with its database/secrets/service identity, then migrate and enable the integrated Order deployment through reviewed plans.

[Section 12.1 — Client foundation](section-12-1-local-inventory-client.md)

[Section 12.2 — Durable tracking](section-12-2-durable-submission-tracking.md)

[Back to README](../../README.md)
