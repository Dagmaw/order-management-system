# Section 13.2 — Private Inventory database bootstrap

## Current status

Completed in AWS on 28 November 2026. [Terraform deployment](https://github.com/Dagmaw/order-management-system/actions/runs/46115819153) succeeded with six additions, zero changes, and zero deletions. [Private database bootstrap](https://github.com/Dagmaw/order-management-system/actions/runs/46116062115) succeeded with container exit code 0 and verified the dedicated Inventory database/login over a certificate-verified connection. Java service activation is the next section.

## 1. What the bootstrap does

Run the DatabaseSetup image with `DB_SETUP_OPERATION=initialize-inventory`. It connects to the PostgreSQL maintenance database named `postgres` using an administrator login and certificate verification (`sslmode=verify-full`). It creates only the logical database `inventory` and its dedicated login `inventory_app` on the existing PostgreSQL instance.

The login owns its own service database so Flyway can apply Inventory migrations when Java starts. It receives no superuser, role-creation, database-creation, replication, or row-security bypass privileges. Membership in other roles is rejected. Database access for PUBLIC is revoked. This is logical isolation on the shared RDS instance, not a second RDS instance.

The operation refuses an existing database with another owner or an existing login without the project ownership marker. It does not drop databases, transfer foreign ownership, run Orders migrations, or grant an Inventory login access to Orders tables.

A session advisory lock serializes bootstrap attempts. The physical administrator connection is not pooled, so closing it releases the lock after success or failure. Role configuration uses a transaction; database creation is separate because PostgreSQL does not allow CREATE DATABASE inside a transaction. If execution stops after creating the role, rerun with the same managed secret to complete initialization.

## 2. Required task settings

| Setting | Value/source |
| --- | --- |
| DB_SETUP_OPERATION | initialize-inventory |
| PGHOST | Existing private RDS hostname |
| PGDATABASE | postgres |
| PGUSER / PGPASSWORD | RDS-managed administrator secret, injected into the one-off task |
| PGAPPUSER / PGAPPPASSWORD | Dedicated Inventory database secret, injected into the task |
| PGSSLROOTCERT | /app/certs/global-bundle.pem |

The application password must contain at least 32 characters. Do not paste passwords into workflow inputs, command arguments, source files, Terraform variables, or logs. The future AWS workflow must launch a fixed private task definition, with credentials injected by ECS, behind the existing environment approval gate.

## 3. Run the isolated verification

1. Start Docker Desktop with Linux containers.
2. Open a terminal in the repository root.
3. Run `python scripts/test-inventory-bootstrap.py`.
4. The script generates a temporary TLS certificate and random test credentials, builds a disposable PostgreSQL 18 container and the DatabaseSetup container, and creates an isolated Docker network. It publishes no host ports.
5. It tests bootstrap using a non-superuser operator with CREATEDB and CREATEROLE, rather than relying on PostgreSQL superuser behavior.
6. It verifies ownership, repeated execution without data loss, concurrent attempts, denied cross-service table access, and denied Orders-login access to Inventory.
7. It verifies rejection of invalid login/database/password/certificate settings and foreign role/database ownership.
8. It removes its own containers, volumes, temporary images, private key, and credential configuration. Existing application containers are left running.

CI runs these checks before the existing .NET–Java integration and recovery tests. Generated files live under the ignored `.ci-integration` directory and are excluded from the main Docker build context.

## 4. Remaining AWS steps

1. Merge the verified bootstrap implementation.
2. Publish a new DatabaseSetup image and record its immutable ECR digest. Do not change the existing Orders migration task image.
3. Prepare a separate Inventory bootstrap task definition and execution role, scoped to that image, its log group, and the two required secrets. The application task role needs no AWS permissions.
4. Review the exact IAM and Terraform changes before deployment.
5. Run the one-off task in the private app subnets with the Inventory security group through the approved workflow. Never retrieve the live database secrets locally.
6. Confirm a successful task exit and the sanitized database/login verification message.
7. Only then proceed to Java task activation, Flyway migration/readiness checks, and .NET integration configuration as separate deployment steps.

No recurring Java task charge begins from the local bootstrap checks. Existing AWS resources remain running.

## 5. Prepared AWS deployment (28 November 2026)

The successful publication run is [Publish Database Setup Image](https://github.com/Dagmaw/order-management-system/actions/runs/46114567498). The task uses this immutable image:

```text
638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:e25a934e7816c06da46b5e2c05b343925bc176db8c1f4287e1da89fb9a1415c8
```

A live Terraform preview confirmed **6 additions, 0 updates, 0 deletions**:

- Three dedicated roles: bootstrap execution, bootstrap application task, and bootstrap workflow.
- Two inline permission policies, for the execution and workflow roles. The application task role has no AWS permission policy.
- One task definition: `order-management-dev-inventory-bootstrap`, with 0.25 vCPU and 0.5 GiB memory. No ECS service is created.

The execution role can pull from the existing API ECR repository, write only to the Inventory setup log group, and retrieve only the RDS administrator and Inventory database secrets. ECS injects these credentials into the one-off task. The workflow role can run only the Terraform-created task revision on the existing cluster, inspect task status, and pass only its two task roles to ECS. It cannot retrieve secret values.

The workflow trust uses the repository's immutable GitHub subject and the existing `terraform-dev` environment. Main is required by the workflow. No credential or operation overrides are accepted by the launcher. Public IP assignment is disabled.

### Permission review and deployment sequence

1. Review `infra/aws/dev/inventory-bootstrap.tf`, the two `infra/iam/inventory-bootstrap-terraform-*.json` files, and `scripts/initialize-inventory-bootstrap-permissions.ps1`.
2. Approve the new IAM permissions and six-resource deployment before installing the supplemental policies. The existing Inventory Terraform policies remain unchanged. The supplements add only bootstrap task metadata/lifecycle permissions and permission to pass its two task roles to ECS.
3. After approval, run `scripts/initialize-inventory-bootstrap-permissions.ps1` using the project AWS profile. It verifies the account, creates the two named policies if absent, and attaches each only to the matching Terraform plan/apply role. It refuses to overwrite differing existing policies.
4. Merge the reviewed task/workflow changes after CI passes.
5. Start Terraform AWS Dev with apply enabled. Review the fresh plan and approve `terraform-dev` only if it still contains the six bootstrap additions. The local preview is not the saved plan used by the approved workflow.
6. Read the non-secret `inventory_bootstrap` Terraform output. Configure these GitHub repository/environment variables from that output:

| GitHub variable | Terraform output field |
| --- | --- |
| INVENTORY_BOOTSTRAP_TASK_DEFINITION | task_definition_arn |
| INVENTORY_BOOTSTRAP_SUBNET_IDS | subnet_ids |
| INVENTORY_BOOTSTRAP_SECURITY_GROUP_ID | security_group_id |

7. Start **Run Inventory Database Bootstrap** from main. Review and approve its `terraform-dev` environment deployment. The workflow assumes the dedicated bootstrap role and runs exactly one task in the private app subnets.
8. Require the workflow to succeed and the `inventory-bootstrap` container to stop with exit code 0. Its application success message means the database owner, dedicated login, and certificate-verified application connection were checked. Inspect `/ecs/order-management-dev/inventory-setup` privately if it fails; do not paste credential-bearing diagnostics into issues.
9. If a task fails after role creation but before database creation, correct the cause and rerun the same operation with the same managed secret. Do not delete the role or database. Foreign ownership requires investigation; the bootstrap deliberately refuses a takeover.
10. Keep `inventory_image` empty until the separate Java activation step is reviewed. Leave the existing API and Orders migration image pins unchanged.

The six resources add no continuously running task. Executing bootstrap incurs one-off Fargate and related usage charges; it does not activate the monthly Java service capacity.

### Verification performed

- Terraform formatting and validation passed.
- Live preview confirmed exactly six bootstrap additions and no updates or deletions.
- Five offline launcher checks passed: fixed task/private network/no secret overrides, incorrect task rejection, incorrect account rejection, failed exit rejection, and status inspection after waiter failure.
- The merged bootstrap implementation already passed disposable TLS PostgreSQL 18 checks, including non-superuser execution, concurrency, isolation, repeat runs, certificate hostname rejection, and foreign ownership protection.

## 6. Completion evidence

- Task definition: `order-management-dev-inventory-bootstrap:1`.
- Task: `8663f311aef049f08b8471971862a23b` in cluster `order-management-dev`.
- Container status: STOPPED, exit code 0.
- Success output: `Inventory database and dedicated login verified by the bootstrap task.`
- Three non-secret workflow settings were configured in the `terraform-dev` GitHub environment from Terraform outputs.
- No live administrator or Inventory database credentials were retrieved locally.

The preparation instructions above record the deployment sequence. Their approval and execution steps are now complete. Proceed to Section 13.3 for Java activation; the Orders integration deployment remains separate.
