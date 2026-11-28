# Section 13.7 — Live AWS Orders integration smoke checks

## 1. Deployment evidence

[Orders activation run 46120849040](https://github.com/Dagmaw/order-management-system/actions/runs/46120849040) completed successfully on 28 November 2026. The API service uses `order-management-dev-api:3`; Java uses `order-management-dev-inventory:1`. Both ECS services reported desired/running/pending 1/1/0 and COMPLETED deployments.

The live API task definition was checked for the reviewed image digest, `Inventory__Enabled=true`, ClientCredentials mode, private HTTPS base URL, Cognito token endpoint/client ID, and `inventory/reserve` scope. `Inventory__ClientSecret` is an ECS-injected secret field. No secret value was retrieved locally.

## 2. Controlled test scope

The manual check uses the public development API at `https://api-dev.kaddigitalsolutions.com`. It generates fresh test Customer credentials in memory, retains no passwords or tokens in its report, and uses a UUID idempotency key for each logical request.

The initial run registered two Customers and created one accepted two-unit order. A targeted corrected stock probe registered one additional Customer and created no accepted order. These three test Customer records, the accepted order, and submission/audit records remain in the development database. No stock reset, reservation release, record deletion, service stop, or outage simulation was performed.

## 3. Observed public API results

| Check | Observed result |
| --- | --- |
| API health | HTTP 200 Healthy |
| Product 1 lookup | Success; price 30.00 |
| Fresh Customer registration/login | Success |
| Anonymous orders access | 401 |
| Customer access to user administration | 403 |
| Order without Idempotency-Key | 400 |
| Create two-unit order | 201; order ID 2; frozen total 60.00 |
| Replay same key and payload | Same order ID and total |
| Change payload under the same key | 409 |
| Owner order lookup | 200 |
| Different Customer order lookup | 404 |
| Quantity zero | 400 |
| Within-limit insufficient stock request | 409; submission state Compensated |
| Rejected Customer's order list | Empty; no accepted order created |

The first stock probe incorrectly used quantity 1,000,000, above the DTO's maximum of 1,000. That assertion failed and did not provide stock-rejection evidence. The test input was corrected to 1,000, and only the stock-check subset was rerun. The corrected subset passed. The initial failed assertion is retained in its original sanitized report; it is not rewritten as a passing run.

The original run has 15 passing checks before that stock assertion. The targeted rerun has five passing checks. The final full-suite order-count assertion was not reached in the original run; the empty-order-list assertion was verified for the corrected rejection Customer.

## 4. Repeat the manual checks deliberately

The reusable script is `scripts/smoke-aws-orders.py`. It is not added to automatic CI because it creates persistent development test records and consumes stock for its accepted order. Its endpoint is fixed to this project's AWS development API.

Before running it, verify the deployed API has integration enabled, the correct image, and healthy Java readiness. The stock probe assumes the training product has less than 1,000 available units; if the shared fixture has been replenished, review the probe before running it. Do not run against a production catalog or treat it as a load test.

From the repository root, choose a private local report destination:

```text
python scripts/smoke-aws-orders.py --run --report <local-report-path.json>
```

To run only the corrected rejection subset, creating one test Customer and no expected accepted order:

```text
python scripts/smoke-aws-orders.py --run --stock-check-only --report <local-stock-report-path.json>
```

`--run` is required so inspecting the script or its help cannot accidentally create test records. Use a distinct report destination for each run. Reports contain test results, IDs, timestamps, and the idempotency key, but no login credentials or machine secrets.

## 5. What remains to prove privately

Successful public order creation with the enabled production adapter, replay behavior, and stock rejection demonstrate the integrated API behavior. They do not independently measure private stock before/after or inspect the stored reservation.

Next, prepare a narrowly scoped, approval-gated verification task inside the VPC to check reservation status and stock deltas. Use a dedicated verification path and short-lived machine token; do not retrieve the live Cognito client secret locally, open the private load balancer publicly, reuse the administrator database bootstrap task for convenience, or stop either service.

The private check should establish exactly one reservation/stock decrement for one accepted logical order and none for the rejected order, without resetting shared state. Concurrent reservation, outage/recovery, deployment automation, and operational handoff remain separate milestones; isolated local/CI coverage must not be relabeled as completed AWS failure drills.

## 6. Follow-up private verification completed

The pending stock-delta verification subsequently passed inside AWS in [run 46124842279](https://github.com/Dagmaw/order-management-system/actions/runs/46124842279). Order 3, total 60.00, changed product 1 from 98/version 1 to 96/version 2; replay, conflict, and rejection snapshots remained 96/version 2. See Section 13.9 for the complete evidence and retained test records. The earlier public smoke results and corrected-probe history remain unchanged.
