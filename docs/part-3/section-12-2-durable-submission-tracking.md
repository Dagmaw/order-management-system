# Section 12.2 — Durable Order Submission Tracking

## Status — 27 November 2026

Tracking storage and an additive EF migration are implemented and tested. This is prerequisite infrastructure for the integration workflow. No order HTTP endpoint, automatic recovery worker, or Inventory-driven order creation has been enabled yet. The migration was applied only to disposable verification databases, not local application or AWS databases.

Section 12.1 implemented the authenticated Inventory client. This section provides persisted identity, snapshots, and concurrency leases. The next stage must connect both to an order coordinator and recovery worker.

## Why this exists

A timeout does not tell us whether a remote service committed its operation. A process can also stop after reserving stock but before saving an order. Retrying with a new reservation ID can deduct twice; blindly releasing after an uncertain order commit can leave an existing order without stock.

An order submission is therefore a durable attempt, separate from a completed Order.

## Persisted data

| Field | Purpose |
|---|---|
| Id | Stable UUID reserved for this attempt; will be used as its Inventory reservation identity |
| ActorAccountId + RequestKey | Unique authenticated caller/idempotency identity |
| UserId | Intended business user/order owner |
| RequestHash | Canonical request fingerprint supplied by the future validated coordinator |
| SnapshotJson | First immutable item/price snapshot supplied by that coordinator |
| State | Pending, Reserved, Compensating, Compensated, Rejected, or future atomically Completed |
| LeaseOwner + LeaseUntilUtc | One worker's current processing lease |
| CreatedAtUtc / UpdatedAtUtc / Version | Progress and change tracking |

The store checks identity shape and fingerprint format. Business validation, canonical hashing, authenticated actor extraction, and price snapshot creation must be implemented by the coordinator; they are not supplied by a public request body directly.

## Retry rules

1. A first attempt persists one UUID, fingerprint, and snapshot.
2. Same actor/key/target user/fingerprint returns the existing record and its original snapshot, even if catalog prices have changed since then.
3. Reusing the actor/key with a different fingerprint or target user returns a conflict.
4. Different actors have separate request-key namespaces.
5. Owned lookup cannot retrieve another actor's submission.
6. Concurrent inserts are constrained by a database unique index. The loser reads and validates the committed winner after its failed insertion rolls back.

The HTTP API does not accept an idempotency header yet; these rules are tested at the storage boundary.

## Worker leases and transitions

Leases are acquired with one conditional database UPDATE, not an in-memory lock. Duration is restricted to 10 seconds–5 minutes. Unexpired leases block other workers. After expiry, another worker can acquire the record with a different lease identity.

Progress updates require the matching lease identity, an unexpired lease, and the expected current state. A stale worker cannot update or clear a newer worker's lease. Terminal records cannot be reacquired.

Currently allowed store transitions:

```text
Pending → Reserved → Compensating → Compensated
Pending → Rejected
```

The generic transition method deliberately cannot set Completed. The next stage must insert the Order and mark the submission Completed in the same Order-database transaction, with a uniqueness constraint linking the order to the submission. This prevents a record from claiming success without its order.

A lease alone does not cancel an already dispatched HTTP request. The future coordinator must still handle delayed responses and reconcile the same reservation identity. Durable storage alone is not a finished saga/recovery mechanism.

## Migration

AddDurableOrderSubmissions creates only the OrderSubmissions table and its unique identity/progress indexes. It does not drop or rewrite existing Orders, Products, Users, or Identity data. The generated Down operation drops the new table if deliberately rolling back; it is not executed by this milestone.

A design-time DbContext factory supports migration generation without running API startup or connecting with live credentials. It reads OMS_MIGRATION_CONNECTION or the existing ConnectionStrings__DefaultConnection environment value. Its fallback is a design-only placeholder, not a working production connection.

Do not apply this migration to AWS yet. Review and apply it to the local application database as part of enabling/testing the next integrated workflow. No schema deployment is needed merely to continue using the unchanged existing order flow.

## Verification

- Full .NET suite: 72 passed (13 Application, 59 API/client/store tests).
- All 6 store tests passed against isolated PostgreSQL 18 using actual EF migrations.
- Same-key replay preserves UUID and original price snapshot across DbContext instances.
- Changed payload/target user conflicts; another actor cannot read the record.
- Lease expiry transfers ownership and blocks stale updates/releases.
- Terminal states cannot be reacquired; standalone Completed transitions are rejected.
- Eight concurrent same-key PostgreSQL requests produced one submission identity; eight concurrent lease acquisitions produced one winner.
- SQLite covers ordinary store behavior. Its concurrency test does not simulate PostgreSQL writers; real concurrency was verified separately on PostgreSQL.

To run normal checks:

```powershell
dotnet test OrderManagementSystem.sln --configuration Release
```

The optional OMS_SUBMISSION_TEST_POSTGRES test mode is destructive and is restricted by the fixture to a dedicated localhost verification database. It creates uniquely named disposable databases, migrates them, and removes them after each test. Never provide application/AWS database credentials to it.

## Next integration stage

1. Build validated canonical request fingerprints and immutable product price snapshots.
2. Persist submission before contacting Inventory.
3. Coordinate reserve, order persistence, and necessary compensation using a stable ID.
4. Add atomic order/submission completion and uniqueness protection.
5. Add owner-scoped API responses for completed, pending, and rejected attempts.
6. Recover pending work after restart; reconcile uncertain Inventory and database outcomes.
7. Test full local HTTP order-to-Inventory behavior and deliberate failures before AWS integration.

[Section 12.1 — Client foundation](section-12-1-local-inventory-client.md)

[Back to README](../../README.md)
