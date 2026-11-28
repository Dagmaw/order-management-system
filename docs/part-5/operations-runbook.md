# Operations and recovery runbook

Applies to the development deployment recorded on 28 November 2026. Keep the project AWS resources and main local services running while the project is in progress. This runbook documents controlled procedures; no destructive recovery or outage drill was performed to create it.

## 1. Inventory and first checks

| Item | Current location |
| --- | --- |
| AWS account / region | 638500336727 / us-east-1 |
| ECS cluster | order-management-dev |
| Orders service | order-management-dev-api |
| Inventory service | order-management-dev-inventory |
| Public API | https://api-dev.kaddigitalsolutions.com |
| Swagger UI | https://api-dev.kaddigitalsolutions.com/swagger/index.html |
| Inventory | Private: https://inventory.dev.internal.kaddigitalsolutions.com |
| Database instance | order-management-dev-postgres |
| API logs | /ecs/order-management-dev/api |
| Inventory logs | /ecs/order-management-dev/inventory |
| Orders migration logs | /ecs/order-management-dev/database-setup |
| Inventory setup logs | /ecs/order-management-dev/inventory-setup |
| Verification logs | /ecs/order-management-dev/inventory-verification |

Start with the latest Terraform workflow's sanitized `deployment-verification` report. For each service, check its primary rollout state, task revision, desired/running/pending counts, and target health. A normal snapshot is completed deployment and 1/1/0; do not rely on an old screenshot if a new rollout is underway.

Use GET `/health`, GET `/api/products`, and the OpenAPI document for non-mutating public checks. Anonymous `/api/auth/me` and `/api/orders` should return 401. Do not open the private Inventory endpoint publicly to troubleshoot from a laptop.

## 2. Request triage

| Symptom | First investigation | Safe next action |
| --- | --- | --- |
| 401 without token | Expected authentication boundary | Log in through the normal Customer flow if authentication is intended |
| 403 with Customer token | Roles/scopes and requested operation | Check permission requirements; do not promote the user just to bypass the check |
| 404 on another Customer's order | Ownership protection | Verify the caller owns that order; do not infer deletion |
| 400 on order submission | Body validation and UUID Idempotency-Key | Correct the request before resubmission |
| 409 with changed payload/key | Actor/key already identifies a different logical request | Do not silently reuse that key for changed contents |
| 409 compensated stock rejection | Inventory availability/business result | Inspect the submission result; no accepted order is expected |
| 202 pending submission | Dependency uncertainty or unfinished durable processing | Follow the returned submission location and retry the same logical request/key, not a new key |
| 5xx or unexpected timeout | API logs, response correlation ID, service/target health | Preserve request/key and inspect dependency failures before retrying |

Record timestamps, safe order/submission IDs, the request's correlation ID when supplied, and the workflow/task revision. Do not paste passwords, JWTs, client secrets, database URLs with credentials, or raw account/customer payloads into incidents. Correlation logging exists; full distributed tracing is not claimed.

## 3. Deployment failures

1. Identify the failing workflow step: plan, environment approval, apply, stability wait, or post-apply gate.
2. A waiting `terraform-dev` job needs the designated reviewer. Do not bypass the environment check with a local apply or alternate role.
3. A saved-plan hash or stale-plan failure requires a fresh plan and review. Do not overwrite the state or reuse a stale binary plan.
4. If ECS service creation/update returns AccessDenied while waiting, determine whether the remote deployment actually exists and is healthy. The Inventory incident in Section 13.3 was a missing deployment-read permission, not a failed application.
5. Inspect Terraform state for a taint only after confirming the intended remote identity, task revision, stable deployment, and healthy targets. Clear only a verified erroneous replacement marker; do not routinely untaint failures.
6. If the automatic gate fails after apply, the infrastructure may already be applied. Read the named failed check before planning recovery. A reverted task revision or wrong digest must not be labeled a successful release.

Do not rerun a failed apply against the old saved plan after state changed. Never delete a healthy ECS service to make a waiter succeed.

## 4. Task or readiness failures

- **Task never starts:** inspect ECS events for ECR pull, execution-role, secret JSON field, network egress, or capacity failure.
- **RDS TLS/login error:** check the private hostname, dedicated login/secret reference, CA file readability, and database security-group path. Keep hostname/CA verification enabled.
- **Java migration failure:** review Flyway output in Inventory logs. Preserve history and apply a reviewed corrective migration; do not edit an already applied migration.
- **OAuth failure:** check the Cognito pool issuer/token endpoint, configured machine client ID, reserve scope, and outbound HTTPS. The Customer JWT issuer is separate. Do not retrieve the machine secret locally.
- **ALB target unhealthy:** check the task port, ALB-to-task security-group rule and health path. API uses `/health`; Java readiness includes its database check.
- **Recovery errors:** inspect durable submission states and the recovery worker's error type. Preserve actor/key/submission identity and leases; do not manually force a Completed state or release a successful order's stock.

Use the controlled private verification task for stock evidence when specifically approved. Do not make stock-changing tests a routine incident probe.

## 5. Release and rollback procedure

For each release, retain the tested source commit, published digest, migration compatibility, approved plan/run, task revision, and verification report. Publishing a new image alone does not change the running pin. Use Section 14 for the exact workflow.

The ECS circuit breaker handles unhealthy rollouts where configured. Operational rollback still needs compatibility review: an old API that ignores Inventory can create orders without reservations even if its health endpoint is green. Schema rollback is not automatically safe for new data. Prefer a reviewed forward correction when it preserves submitted orders and reservations.

If selecting a prior known compatible image, pin its immutable digest in a PR, review the fresh Terraform diff, approve the deployment gate, and require the automatic runtime checks plus appropriate business verification. Do not change a tag in place or make an unrecorded console edit.

Review credential rotation as a deployment change through the authorized execution-role/task injection path. Keep secret values out of Terraform variables, workflow inputs, source files, and local test helpers. Do not grant the running application an administrator database login for convenience.

## 6. Backup, restore and retention boundary

Current Terraform config is a single-AZ PostgreSQL 18 db.t4g.micro instance with encrypted 20 GiB gp3 storage and seven-day automated backups. Orders and Inventory have separate logical databases/logins on that instance. Log groups use seven-day retention; historical GitHub artifacts may also expire.

A database restore drill has not been performed. Before a controlled drill, review snapshot availability, the separate restore target and additional cost, network/logins, validation data, and a cutover/backout plan. Restore into a separate reviewed instance first and verify both databases, schema history, users, orders, submissions, and reservation/stock consistency. Do not overwrite or delete the current instance to test a restore.

The development RDS configuration currently has deletion protection disabled and skips a final snapshot on deletion. Accordingly, teardown is not an approved operational action while the user wants the project running. A later teardown requires explicit approval and a reviewed data-preservation plan; do not run terraform destroy as routine cleanup.

## 7. Daily project operation and escalation notes

- Review service events, target health, recovery warnings, and recent failed workflow checks.
- Review Billing/Budgets and the verified notification recipient; no current bill amount is asserted by this document.
- Check resource retention and known development fixtures before repeating mutating tests.
- Keep source/image/task/migration/run evidence together. Refresh documentation after material changes.
- Treat log retention, restore testing, monitoring/alerts, least-privilege review and higher availability as explicit follow-up work before production use. They are not implied complete by successful smoke checks.

Preserve both application services during triage. Do not stop them for an outage demonstration without a separately approved test window and recovery procedure.
