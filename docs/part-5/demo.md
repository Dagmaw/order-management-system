# Repeatable project demo

Use this as a 12-15 minute walkthrough. The default walkthrough is read-only. AWS resources stay running; no administrator password or machine token is displayed.

## 1. Explain the business flow — 2 minutes

Open the [architecture](architecture.md). Explain two services with separate logical databases on one private RDS instance: Customers submit orders to .NET; .NET freezes product prices and reserves stock through Java using a Cognito machine token. Durable submission state and idempotency bridge the two local transactions. Customer login remains the API's Identity/JWT flow.

Point out the public API ALB, private Inventory ALB, private task subnets, and scoped execution-role secret injection. Explain the single-task/single-AZ development limits without claiming production high availability.

## 2. Show the public contract — 3 minutes

1. Open the public Swagger UI from the README.
2. Show public product reads and protected mutation/order/user routes.
3. Execute a product read and show its price.
4. Execute `/api/auth/me` without authorization and show 401 as the expected boundary.
5. Show request validation and Idempotency-Key in the order contract; do not paste a previously shared administrator credential or a machine client secret.

No account/order creation is needed for this portion. If an interactive authenticated demo is desired, plan it separately with a fresh test Customer and a clear retained-test-data policy.

## 3. Show the approved release — 3 minutes

Open [workflow 46126700549](https://github.com/Dagmaw/order-management-system/actions/runs/46126700549).

- Point out the source commit, fresh plan, environment approval and exact saved-plan apply.
- Show that this demonstration changed zero infrastructure resources.
- Show automatic stable-service/task-revision/image/target checks.
- Show public health/OpenAPI and anonymous-access checks.
- Open the sanitized `deployment-verification` artifact if retained, or the successful job log. All 17 checks passed.

Explain that image publication and deployment are separate: a tested digest is deliberately pinned and reviewed. Terraform-only commits do not require changing the application pin.

## 4. Show real stock/idempotency evidence — 3 minutes

Open [private verification 46124842279](https://github.com/Dagmaw/order-management-system/actions/runs/46124842279) and Section 13.9.

- Verifier ran once inside the VPC with no public IP, secret read, or database access.
- Order 3 had total 60.00 for two units.
- Actual stock/version: 98/1 before, 96/2 after, unchanged after replay, conflict and insufficient-stock rejection.
- The task exited 0 and both application services remained 1/1/0 with completed deployments.

These are recorded historical snapshots, not a promise that current stock is still 96. Do not release that accepted reservation or reset shared stock to reproduce a screenshot.

## 5. Explain failures and recovery — 2 minutes

Use the [operations runbook](operations-runbook.md). Explain 202 durable pending work, same-key retries, 409 business rejection/compensation, ownership 404, and the difference between a failed waiter and a failed application.

Point to the isolated CI concurrency/outage/restart-recovery tests. State clearly that those disruptive scenarios used disposable services; the real AWS verification did not stop services or perform a load/restore drill.

## Optional approved live repetition

To repeat real private stock verification, review Section 13.9 and a quiet test window first. Start Run Private Stock Verification on main and obtain its required terraform-dev approval. It creates one fresh Customer and one accepted two-unit order, plus a compensated rejection. The records remain in development and the new quantities will differ.

Do not rerun the mutating task merely to check health. Use the automatic read-only gate or public GET health/product checks for routine checks. Do not expose Inventory publicly or fetch live machine credentials to make the demo easier.

## Close with engineering tradeoffs

Explain the current deployment's verified capabilities and development limits. Be prepared to discuss why durable state plus idempotent operations are used instead of a cross-service transaction; how issuer/client/scope validation separates Customer and machine identities; how a pipeline distinguishes apply success from runtime acceptance; and what restore/alerting/availability work would be required before production.
