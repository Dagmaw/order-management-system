# Section 13.4 — Prepare Orders database for AWS Inventory integration

## Status and deployment order

The Java Inventory service is healthy in AWS. Orders is still running its earlier API image, with Inventory integration disabled. This section prepares the Orders migration task; it does not enable Inventory calls or deploy a new API image.

Use this order: update the migration task definition, run the additive migrations successfully, then prepare and review the API integration deployment. Do not enable the recovery worker before its database tables exist.

## 1. Migration image and scope

The existing standalone DatabaseSetup task is updated to the image published and tested during Section 13.2:

```text
638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:e25a934e7816c06da46b5e2c05b343925bc176db8c1f4287e1da89fb9a1415c8
```

Although this image also supports `initialize-inventory`, the existing Orders task continues to target `ordermanagement` and uses `migrate`. It receives the existing Orders application secret and RDS administrator secret through ECS injection. No new secret access or IAM changes are proposed.

The Terraform change updates only `local.database_setup_image` in `infra/aws/dev/database-task.tf`. Creating a new task-definition revision is displayed as replacement of that task definition. It does not replace RDS, the Orders API service, or Java Inventory. It does not execute a migration automatically.

## 2. Reviewed additive migrations

| Migration | Forward changes |
| --- | --- |
| 20261128023137_AddDurableOrderSubmissions | Creates OrderSubmissions and indexes for actor/request-key uniqueness and recovery lookup. |
| 20261128024038_LinkOrdersToInventorySubmissions | Adds nullable FailureReason to OrderSubmissions, nullable InventorySubmissionId to Orders, and a unique index on InventorySubmissionId. |

The forward methods do not delete existing tables, columns, orders, or users. Existing orders can retain a null InventorySubmissionId. The currently deployed API can continue using its existing columns. PostgreSQL schema changes can briefly take locks; use the controlled one-off migration workflow and check application health afterward.

DatabaseSetup checks and applies pending EF migrations, then configures `order_app` DML and sequence permissions for all application tables. It revokes access to EF migration history from that login and verifies the application connection. Credentials must remain in ECS secret injection; do not fetch them locally.

## 3. Deploy the task definition

1. Review and merge this change after CI succeeds.
2. Start **Terraform AWS Dev** on main with apply enabled.
3. Check the fresh saved plan. It must change only `aws_ecs_task_definition.database_setup`, normally **1 to add, 0 to change, 1 to destroy** because an immutable task-definition revision is replaced.
4. Do not approve changes to the API service, Inventory service, RDS instance, or secret values in this prerequisite step.
5. At **Review deployments**, select `terraform-dev` and choose **Approve and deploy** after reviewing the expected task-only plan.
6. Read the new non-secret `database_setup_task_definition_arn` output. Set `DB_SETUP_TASK_DEFINITION_ARN` to that exact new revision in the GitHub configuration used by **Run Database Setup**. Check environment variables as well as repository variables because environment values take precedence.
7. Retain the existing DB_SETUP_SUBNET_IDS and DB_SETUP_SECURITY_GROUP_ID private-network settings. This is the Orders migration task, not the Inventory bootstrap task.

## 4. Run the migrations

1. In GitHub Actions, open **Run Database Setup**.
2. Choose main and operation **migrate**. Leave admin email/user ID blank; grant-admin is not part of this step.
3. Start the run and review the `terraform-dev` approval before allowing database changes.
4. The workflow launches exactly one private Fargate task using the reviewed task revision. It waits for STOPPED and requires container exit code 0.
5. Require the sanitized output to show migration completion and application-login verification. Two pending migrations are expected if AWS was at the previously deployed Identity migration; EF migration history determines the actual pending count.
6. If it fails, inspect `/ecs/order-management-dev/database-setup` privately. Diagnose the specific migration or permission failure; do not paste SQL containing passwords into issues and do not drop or recreate the RDS instance.
7. A successful rerun should report zero pending migrations. Rerun only if needed to resolve an interrupted or failed operation; routine duplicate runs are unnecessary.
8. Check the public Orders `/health` endpoint still returns HTTP 200 Healthy and Inventory still has one healthy running task.

## 5. Completion evidence to record

- New Orders migration task-definition revision and pinned image digest.
- Successful Terraform task-definition update run.
- Successful migration workflow and task exit code 0.
- Names/count of migrations applied, with existing API health afterward.
- No live credentials retrieved locally and no Inventory integration enabled prematurely.

## 6. Next reviewed deployment

After migration success, prepare the .NET API release containing the tested durable submission/recovery and OAuth adapter. Verify the non-root API container can read the RDS CA bundle. Configure:

- Inventory enabled with private HTTPS base URL `https://inventory.dev.internal.kaddigitalsolutions.com/`.
- Authentication mode ClientCredentials, the existing Cognito token endpoint/client ID, and scope `inventory/reserve`.
- Client secret injected from the dedicated Orders service-identity secret. Extend only the API execution role to retrieve that dedicated secret; the API application task role needs no secret retrieval permission.
- A reviewed API image digest and rollout stability checks, including scoped API deployment-observation permissions if required by the provider.

That release requires its own Terraform review. Then verify real AWS order creation reserves stock, idempotent replay does not reserve twice, and failures/recovery behave correctly. Private Java hosting and database readiness alone do not establish that the running Orders API is integrated.
