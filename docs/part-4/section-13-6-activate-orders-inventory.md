# Section 13.6 — Activate Orders to private Inventory in AWS

## Status

Deployed successfully in [run 46120849040](https://github.com/Dagmaw/order-management-system/actions/runs/46120849040) on 28 November 2026 after explicit IAM/rollout approval. The API is running task revision 3 with the reviewed image and private Inventory integration enabled. Both ECS deployments are completed at desired/running/pending 1/1/0. Section 13.7 records live public API checks; independent private stock/reservation evidence remains a following verification step.

## 1. Verified release prerequisites

- Orders migrations completed in [run 46119087256](https://github.com/Dagmaw/order-management-system/actions/runs/46119087256), with task exit code 0 and application-login verification.
- Java Inventory is running privately behind its healthy internal HTTPS load balancer.
- [Main-branch Build and Test run 46120120982](https://github.com/Dagmaw/order-management-system/actions/runs/46120120982) succeeded and published the API candidate from commit `df4dbe4d6ccd466fa85821474edab4709e443fec`.
- The source includes the non-root RDS certificate readability check and tested durable submission/recovery and OAuth adapter.
- The candidate tag and digest were verified directly in ECR.

```text
638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:105e39a02d0f7975822e7ea3b1d50c9460c82c490580bf8bf2dbe6666bf432c4
```

## 2. Exact deployment change

The activation changes only `local.api_image` and the default of `orders_inventory_enabled`. That flag supplies the private Inventory HTTPS URL, ClientCredentials authentication, the existing Cognito token endpoint/client ID, the `inventory/reserve` scope, and ECS client-secret injection.

Expected Terraform resource actions:

| Resource | Action |
| --- | --- |
| aws_iam_role_policy.api_inventory_identity[0] | Create one scoped execution-role secret policy |
| aws_ecs_task_definition.api | Register new API task revision; deregister old revision |
| aws_ecs_service.api | Update in place to the new task revision |

Expected total: **2 to add, 1 to change, 1 to destroy**. The destroy is the old task-definition revision, not the API service or database. No Java service, RDS, ALB, DNS, or network-resource changes are intended.

The existing API desired task count and CPU/memory allocation remain unchanged. There is no additional steady-state API task capacity in this plan. Token requests, logs, traffic, and temporary rollout overlap still incur usage charges.

## 3. Permission review

Before deployment, approve these narrowly scoped additions:

1. The API execution role can retrieve only the dedicated `order-management/dev/orders-service-identity` secret through the conditional `api-inventory-identity` policy. ECS injects its `clientSecret` JSON field into the API container. No administrator or Inventory database secret access is granted to this role.
2. The API application task role receives no new AWS permissions. It uses the injected credentials to request a short-lived Cognito machine access token.
3. Install `infra/iam/api-deployment-read.json` with `scripts/initialize-api-deployment-read.ps1`. It gives only the Terraform apply role ListServiceDeployments, DescribeServiceDeployments, and DescribeServiceRevisions for the API service and its deployment/revision ARNs. This avoids the earlier missing-waiter-permission failure without granting service mutation or secret access.

The existing Terraform apply role was checked with IAM simulation and already has permission to manage inline policies on the exact API execution role. No broader Terraform IAM policy change is needed for the new inline policy.

Do not retrieve the live machine client secret locally, put it in workflow inputs, or paste it into a test command. The approved verification workflow path remains the way to exercise live machine credentials.

## 4. Deploy through the approval gate

1. Review this activation PR and the exact scopes above.
2. Approve the IAM additions and API rollout.
3. Install the approved API deployment-observation policy using the project account profile. The installer checks the account and refuses to overwrite a differing existing policy.
4. Require CI checks to pass and merge the activation PR.
5. Start **Terraform AWS Dev** on main with apply enabled.
6. Verify the fresh saved plan contains exactly the resource actions above. Do not reuse a local preview or an earlier saved plan.
7. Choose **Review deployments → terraform-dev → Approve and deploy** after reviewing that plan.
8. Terraform waits for the rolling API deployment to stabilize; the existing deployment circuit breaker remains enabled.

## 5. Verify rollout and integration separately

### Service readiness

1. Require successful Terraform apply.
2. Confirm the API primary deployment is COMPLETED, desired/running/pending is 1/1/0, and the task uses the reviewed digest.
3. Confirm the public API target group is healthy and `/health` returns HTTP 200 Healthy.
4. Confirm Java Inventory still has one healthy task and no deployment change.
5. Inspect API startup/recovery logs for configuration or database errors, without printing credentials or token contents.

### Real AWS behavior

Service readiness alone does not prove stock reservation. The following controlled verification is the next milestone:

1. Register a fresh test Customer through the public API and log in without using a previously shared administrator password.
2. Create an order for a known seeded Inventory product with a unique Idempotency-Key, then replay the same key and payload. Require the same order identity and price snapshot.
3. Verify stock/reservation state from an approved private verification task, with a narrowly scoped machine identity. Require exactly one reservation for the order and no duplicate stock decrement.
4. Verify changed-payload key reuse, another Customer's order access, insufficient stock, and appropriate failure/recovery scenarios through controlled tests.
5. Record actual AWS results. Do not reset shared stock, stop services, or create outage drills without a specific reviewed test procedure.

## 6. Recovery guidance

If secret injection fails, check the new execution-role policy and JSON field reference. If OAuth fails, check endpoint/client ID/scope and private task outbound HTTPS; do not dump the secret or token. If Inventory calls fail, check private DNS, ALB TLS/readiness, and security-group reachability.

If a provider waiter fails after a healthy rollout, inspect the remote deployment and Terraform state before planning another apply. Never blindly replace a healthy service because an observation permission was missing.

Keep the migrated submission records and Inventory reservations intact during recovery. Disabling integration or restoring a legacy API can permit orders without stock reservations, so it is not an automatic rollback procedure once integrated traffic begins.
