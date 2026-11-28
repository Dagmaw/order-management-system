# Section 13.3 — Activate Java Inventory privately in AWS

## Status

Running privately in AWS as of 28 November 2026, following explicit running-cost approval. The initial apply created the service but failed its deployment observation check because a read permission was missing. The scoped permission correction is installed; the verified healthy service was preserved and its failed-wait replacement marker cleared. The steps below document deployment and recovery.

## 1. Prerequisites and completed evidence

- Section 13.1 private ALB, DNS, TLS certificate, security groups, roles, logs, repository, and database credential secret are deployed.
- [Section 13.2 infrastructure deployment](https://github.com/Dagmaw/order-management-system/actions/runs/46115819153) succeeded.
- [Database bootstrap](https://github.com/Dagmaw/order-management-system/actions/runs/46116062115) exited with code 0 and verified the dedicated database/login over TLS.
- [Inventory image publication](https://github.com/Dagmaw/order-management-system/actions/runs/46113008933) succeeded. The pinned digest was verified in ECR before preparing this change:

```text
638500336727.dkr.ecr.us-east-1.amazonaws.com/inventory-service@sha256:f087764bdec740e246f424a06b184dcd2b50236b5391ee48291c696f219a9c9e
```

## 2. Exact change and cost review

The change sets the default `inventory_image` to the published digest in `infra/aws/dev/inventory-service.tf`. The existing conditional resources then create one task definition and one ECS service. Existing service settings specify Linux/x86, 0.5 vCPU, 1 GiB memory, desired count 1, private app subnets, and no public IP.

The incremental compute estimate for 730 hours is `(0.5 × $0.04048 + 1 × $0.004445) × 730 = $18.02/month`, before credits, taxes, logs, network traffic, and temporary overlapping deployment tasks. This is additional to the resources already running. Rates are based on the [AWS US East Fargate cost example](https://docs.aws.amazon.com/solutions/latest/prebid-server-deployment-on-aws/cost1.html); see [Fargate pricing](https://aws.amazon.com/fargate/pricing/) for billing details. It is an estimate, not a spending cap.

No new RDS instance, load balancer, NAT gateway, public endpoint, or IAM policy is proposed in this activation change. The existing Orders API image and its database migration task remain unchanged.

## 3. Review and merge

1. Review the single activation setting and this deployment procedure.
2. Confirm approval for the extra continuously running compute cost.
3. Require Terraform validation and application/integration CI to pass.
4. Merge the reviewed PR.
5. Start **Terraform AWS Dev** on main with apply enabled.
6. Confirm the fresh saved plan has exactly two additions: `aws_ecs_task_definition.inventory[0]` and `aws_ecs_service.inventory[0]`, with no changes or deletions elsewhere.
7. Open **Review deployments**, select `terraform-dev`, and choose **Approve and deploy** after reviewing that plan. The apply job uses the privately stored saved plan.

## 4. What happens at startup

1. ECS pulls the pinned image through the existing outbound network route.
2. The execution role injects only the Inventory database username/password. The application task role has no AWS permission policy.
3. Java runs with the production profile and connects to the dedicated `inventory` database using hostname and CA verification.
4. Flyway creates the Inventory tables and reservation tables from the existing migrations. The current development V1 migration seeds product IDs 1/2/3 with quantities 100/50/0. These are training fixtures, not production inventory or a synchronization from Orders products. Do not edit an already applied migration to change them.
5. Cognito access-token validation uses the configured pool issuer, JWKS endpoint, machine-client allowlist, and separate reservation/stock scopes. Production does not use the local RSA key.
6. The internal load balancer probes `/actuator/health/readiness`. Readiness includes database connectivity; liveness does not depend on the database.
7. ECS waits for a stable deployment. The configured circuit breaker fails a deployment that cannot become healthy. On this first activation, there is no earlier Inventory service revision to restore; diagnose a failed deployment before rerunning it.

## 5. Verify the deployment

1. Require Terraform apply to succeed.
2. In ECS cluster `order-management-dev`, open service `order-management-dev-inventory`.
3. Confirm desired count 1, running count 1, pending count 0, and a completed primary deployment.
4. Confirm the task uses the pinned digest and task definition family `order-management-dev-inventory`.
5. Confirm the `ominv-*` target group reports the task healthy. This confirms the internal ALB can reach the Java readiness endpoint and the service can access its database.
6. Review `/ecs/order-management-dev/inventory` for successful startup and Flyway completion. Keep credentials and token contents out of shared logs and screenshots.
7. Check the existing public Orders `/health` endpoint still returns Healthy.

Private service URL:

```text
https://inventory.dev.internal.kaddigitalsolutions.com/
```

This private hostname is intended for the VPC. A browser or Postman session outside the VPC is not the verification method. Do not open public security-group access to make the private service reachable. Authenticated Inventory API behavior and Orders-to-Inventory calls in AWS are verified in the subsequent integration milestone.

## 6. If readiness fails

- No task starts: inspect ECS service events for image, secret-injection, execution-role, or capacity failures.
- Database connection fails: check task-to-RDS security-group rules, the dedicated database/login, and the configured CA path and hostname. Keep TLS verification enabled.
- Migration fails: inspect the Flyway error privately and apply a reviewed corrective migration. Do not drop the shared RDS instance or edit an applied migration.
- Authentication initialization fails: check the Cognito issuer/JWKS/client settings and task outbound HTTPS connectivity.
- Java starts but the target stays unhealthy: check port 8080, ALB-to-task security-group rules, readiness path, and database readiness.

## 7. Following milestone

After private Java hosting is healthy, prepare the Orders database migrations, service-token configuration, private Inventory base URL, and updated .NET image as a separate reviewed deployment. Hosting Java by itself does not mean the running Orders API is reserving stock through it yet.

## 8. Deployment permission correction and recovery

The first activation run [46116859275](https://github.com/Dagmaw/order-management-system/actions/runs/46116859275) created the task definition and ECS service, then failed while waiting for stability: `ecs:ListServiceDeployments` was absent from the Terraform apply role. This was a deployment-observation permission failure, not a failed Java startup.

The correction is `infra/iam/inventory-deployment-read.json`, installed through `scripts/initialize-inventory-deployment-read.ps1`. It grants only `ecs:ListServiceDeployments`, `ecs:DescribeServiceDeployments`, and `ecs:DescribeServiceRevisions`, scoped to this Inventory service and its deployment/revision ARNs. It is attached only to `order-management-dev-terraform-apply`. It grants no task mutation or secret access. These scopes follow the [AWS deployment-viewing permission reference](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/service-deployment-permissions.html). The installer verifies the account and refuses to overwrite a differing existing policy.

### Verified runtime evidence

- Service: `order-management-dev-inventory` in cluster `order-management-dev`.
- Desired/running/pending: 1 / 1 / 0.
- Deployment state: COMPLETED, using task definition `order-management-dev-inventory:1`.
- Target group: healthy on private task port 8080.
- Flyway: two migrations validated and applied successfully, schema version v2.
- Existing Orders API: `/health` returned HTTP 200 and `Healthy`.
- IAM simulation: the Terraform apply role is allowed to list deployments for this exact Inventory service.

### Preserve the service after a failed wait

1. Check ECS service events, task status, load-balancer target health, and startup logs. Do not assume a failed Terraform waiter means service creation failed.
2. Confirm the live service is the intended service, uses the intended task definition, has completed its deployment, and passes readiness.
3. Inspect the state for `aws_ecs_service.inventory[0]`. This failure marked it tainted even though the remote service was healthy.
4. Only after those checks, clear that marker with `terraform untaint 'aws_ecs_service.inventory[0]'`. This changes the state marker only; it does not restart or recreate the service.
5. Run a fresh plan, rather than retrying the old saved plan. Recovery verification [run 46117296782](https://github.com/Dagmaw/order-management-system/actions/runs/46117296782) passed and reported: `No changes. Your infrastructure matches the configuration.`
6. Require the fresh plan to report no infrastructure changes. Investigate any unexpected replacement before approving an apply.

Do not rerun the failed job with the stale saved activation plan, delete the service, or disable stability checks to work around a missing read permission. Orders-to-Inventory integration in AWS remains the following milestone.
