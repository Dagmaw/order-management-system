# Section 13.9 — Deploy the private stock verification task

## Status

Completed in AWS on 28 November 2026. [Infrastructure deployment 46124194238](https://github.com/Dagmaw/order-management-system/actions/runs/46124194238) and [private verification 46124842279](https://github.com/Dagmaw/order-management-system/actions/runs/46124842279) both succeeded after explicit approval. The verifier exited with code 0; both application services remained running and stable. The review/deployment instructions below record the completed procedure.

[Publication run 46123608516](https://github.com/Dagmaw/order-management-system/actions/runs/46123608516) passed the isolated checks and published the verifier from source commit `ade200dc260805ea1593cfee2779510118ce0b66`:

```text
638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:66279c18e711dd9b6b21028b3d8be289659121e3d348a10fea75811ff445c222
```

This digest is a verification image in the existing repository. It must not be assigned to the API or database migration task.

## 1. Proposed resources

The Terraform configuration is `infra/aws/dev/inventory-verification.tf`. It adds:

| Resource group | Count | Purpose |
| --- | --- | --- |
| Dedicated IAM roles | 3 | Task execution, empty application task role, and environment-gated workflow role |
| Inline IAM policies | 2 | Image pull/log writing and fixed task launch/evidence reading |
| ECS task definition | 1 | One-off Linux/amd64, 0.25 vCPU, 0.5 GiB verifier |
| CloudWatch log group | 1 | Dedicated verification log group, 7-day retention |
| Security group | 1 | Dedicated private verifier network identity |
| Egress rule | 1 | Outbound HTTPS for public Orders, image/log services, and private Inventory |
| Inventory ALB ingress rule | 1 | HTTPS only from the dedicated verifier security group |

Expected total: **10 additions, 0 updates, 0 deletions**. The new ALB security-group rule permits only the verifier security group; it does not expose Inventory publicly. No ECS service, database resource, or credential secret is created. Application image pins stay unchanged.

The task runs in the private app subnets with public IP assignment disabled. No database security-group access is added. Its fixed script uses only the approved HTTPS API origins and does not follow redirects.

## 2. Permission scopes

The execution role can authenticate to ECR, pull from the existing project API repository, and write only to its dedicated verification log group. It cannot retrieve secrets. The task role has no AWS permission policy.

The workflow role trusts only the repository's immutable GitHub identity with the `terraform-dev` environment subject. It can run only the Terraform-created task revision on the existing cluster, pass only its two task roles to ECS, inspect task status, and read only the verification-prefixed log streams in the dedicated log group. No arbitrary task or command overrides are accepted.

Two supplemental Terraform policy files add metadata/lifecycle access for this task-definition family and PassRole only for these two task roles. Existing project-scoped Terraform permissions manage the Inventory-prefixed roles/log group and VPC security groups. The supplements are installed with `scripts/initialize-inventory-verification-permissions.ps1`, which checks the account and refuses a differing existing policy rather than overwriting it.

These new permissions and network/task resources require review and approval before installation or apply. The reviewed supplements were installed before the approved deployment.

## 3. Validation performed

- The publishing workflow succeeded after rerunning the existing isolated recovery and verifier tests.
- Ten offline launcher/evidence tests passed, including wrong task/account rejection, failed exit handling, absent evidence, failed checks, incorrect stock version, replay stock change, and rejection of local results presented as AWS evidence.
- Terraform formatting and validation passed.
- The published verifier digest was verified directly in ECR.
- The live plan must contain only the ten additions listed above.

## 4. Deploy after approval

1. Review this PR, the IAM supplements, and the dedicated network rule.
2. Approve the scoped IAM/network/task deployment and one-off development verification run.
3. Install the approved supplements with `scripts/initialize-inventory-verification-permissions.ps1` using the project AWS profile. No live application credentials are needed.
4. Require CI to pass and merge the task/workflow PR.
5. Start **Terraform AWS Dev** on main with apply enabled.
6. Confirm the fresh saved plan contains only the ten verifier additions.
7. Select **Review deployments → terraform-dev → Approve and deploy** after reviewing that plan.
8. Read the non-secret `inventory_verification` output and configure these settings in the GitHub `terraform-dev` environment:

| Variable | Output field |
| --- | --- |
| INVENTORY_VERIFICATION_TASK_DEFINITION | task_definition_arn |
| INVENTORY_VERIFICATION_SUBNET_IDS | subnet_ids |
| INVENTORY_VERIFICATION_SECURITY_GROUP_ID | security_group_id |

No secrets or passwords are configured in these variables.

## 5. Run once in a controlled window

1. Ensure both services are healthy and other development stock tests are quiet. Concurrent stock writes cause the verifier to fail exact delta checks rather than produce misleading success.
2. Open **Run Private Stock Verification** on main.
3. Start the workflow, then review and approve its required `terraform-dev` environment gate.
4. The launcher starts exactly one task with no public IP and waits for it to stop.
5. Require container exit code 0.
6. The launcher reads only that task's dedicated log stream and requires a successful `aws-private` structured report, at least 22 passing assertions, the two-unit/one-version delta, and unchanged replay/conflict/rejection snapshots. A healthy task exit alone is insufficient.
7. Record the accepted order ID and snapshots from the sanitized workflow summary/log output.
8. Confirm both application services remain healthy after the task exits.

This creates one fresh development Customer, one accepted two-unit order and its reservation, and one compensated rejected submission. Those records remain as audit data. No stock reset or reservation release is performed. The task checks public order behavior against private stock snapshots; it does not inspect reservation-table rows directly.

If the task fails after order creation, inspect the saved order ID and private log report before rerunning. Do not automatically rerun the entire verifier or delete/reset shared data. A new run creates a new Customer and may create another accepted order.

## 6. Cost and remaining work

There is no continuously running verification service. Execution incurs one-off Fargate, log, image-storage, and network usage. The application services keep their current task counts and running costs.

The real private task passed and its actual snapshots are recorded below, completing this controlled AWS stock/idempotency verification. Outage drills, concurrent load testing, complete deployment automation, and operational handoff remain separate work.

## 7. Actual AWS completion evidence

Verification workflow: [46124842279](https://github.com/Dagmaw/order-management-system/actions/runs/46124842279).

- Task definition: `order-management-dev-inventory-verification:1`.
- Task ID: `3bdbcf2dadc64392966259b1308b2df9`.
- Container: `inventory-verification`, STOPPED with exit code 0.
- Report run ID: `a57ec353580e40e9a2b742e429e913c7`.
- Mode/result: `aws-private`, passed true.
- Accepted order: **3**, total **60.00**, two units of product 1.
- The launcher's evidence gate accepted the successful report and required all verifier assertions to pass.

| Actual private snapshot | Available quantity | Version |
| --- | --- | --- |
| Initial / before order | 98 | 1 |
| After accepted order | 96 | 2 |
| After same-key replay | 96 | 2 |
| After changed-payload conflict | 96 | 2 |
| After insufficient-stock rejection | 96 | 2 |

This independently verifies a two-unit decrement with one version increment, no duplicate decrement on replay, and no stock/version change from the conflict or compensated rejection. It does not inspect database reservation rows directly.

Post-run ECS checks confirmed both `order-management-dev-api` and `order-management-dev-inventory` at desired/running/pending **1/1/0**, with completed deployments. The one-off verifier stopped normally. No live machine/database secret was retrieved, no stock was reset, and neither application service was stopped.

The fresh verification Customer, accepted order 3 and its reservation, and compensated rejected submission remain in development as audit data. Product 1 now has 96 available units in this recorded snapshot. Future legitimate orders may change that value; do not treat 96 as a permanent fixture assertion.

## 8. Following milestone

Complete deployment automation and operational handoff: make approved image promotion repeatable, wait for service stability, run bounded readiness/smoke checks, and retain sanitized evidence. Preserve the distinction between this successful controlled AWS run and isolated concurrency/outage/recovery coverage. No AWS outage drill or load test was performed in this milestone.
