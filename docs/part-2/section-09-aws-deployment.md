# Section 9 — AWS Deployment and Cleanup

This guide records the exercise through 19 November 2026 and explains the remaining deployment steps. **The AWS exercise was subsequently destroyed to avoid cost.** Successful migrations and the final Elastic IP deletion were shown. A deployed API service, HTTPS load balancer, and public end-to-end tests were not shown. Instructions for those milestones below are remaining work, not completed deployments.

### 9.1 Scope and architecture

The exercise used account `638500336727`, region `us-east-1`, and prefix `order-management-dev`.

```text
GitHub Actions → OIDC → temporary AWS credentials
  ├── Terraform → S3 remote state and locking
  ├── image publishing → ECR
  └── one-off Fargate setup task → Secrets Manager → private RDS

Remaining deployment:
Client → domain/HTTPS → public ALB → private API Fargate service → RDS
```

Local Compose is separate. AWS destruction does not delete local databases or source code. The Inventory Service was not deployed in the demonstrated AWS work.

### 9.2 Folder structure and source links

```text
order-management-system/
├── .github/workflows/
│   ├── ci.yml
│   ├── terraform-lock.yml
│   ├── terraform-dev.yml
│   ├── publish-database-setup.yml
│   ├── initialize-secret.yml
│   └── run-database-setup.yml
├── docs/
│   ├── part-1/                  Sections 1–5
│   └── part-2/                  Sections 6–9
├── infra/
│   ├── learning/
│   └── aws/dev/
│       ├── main.tf
│       ├── network.tf
│       ├── subnets.tf
│       ├── public-routing.tf
│       ├── private-routing.tf
│       ├── database-routing.tf
│       ├── security-groups.tf
│       ├── database.tf
│       ├── ecs.tf
│       ├── database-task-iam.tf
│       ├── database-task.tf
│       └── application-secret.tf
├── src/
│   ├── OrderManagement.API/
│   ├── OrderManagement.Application/
│   ├── OrderManagement.Domain/
│   ├── OrderManagement.Infrastructure/
│   └── OrderManagement.DatabaseSetup/
├── services/inventory-service/
├── tests/
├── compose.yaml
├── terraform-dev.ps1
└── README.md                    Project home and documentation links
```

| Files | Responsibility |
|---|---|
| [main.tf](../../infra/aws/dev/main.tf) | Versions, S3 backend, ECR |
| [network.tf](../../infra/aws/dev/network.tf), [subnets.tf](../../infra/aws/dev/subnets.tf) | VPC and six subnets |
| [public](../../infra/aws/dev/public-routing.tf), [private](../../infra/aws/dev/private-routing.tf), [database routing](../../infra/aws/dev/database-routing.tf) | Internet, NAT, isolated database routes |
| [security-groups.tf](../../infra/aws/dev/security-groups.tf) | ALB, API, database traffic |
| [database.tf](../../infra/aws/dev/database.tf) | Private RDS and subnet group |
| [ecs.tf](../../infra/aws/dev/ecs.tf) | Cluster and setup logs |
| [database-task-iam.tf](../../infra/aws/dev/database-task-iam.tf) | Execution and task roles |
| [database-task.tf](../../infra/aws/dev/database-task.tf) | Image digest, secret injection, task ARN output |
| [application-secret.tf](../../infra/aws/dev/application-secret.tf) | Application secret metadata |
| [DatabaseSetup Program.cs](../../src/OrderManagement.DatabaseSetup/Program.cs) | Migrations and application login |
| [API Program.cs](../../src/OrderManagement.API/Program.cs) | API startup, database configuration, Identity/JWT |
| [CI](../../.github/workflows/ci.yml) | Application builds, tests, image publishing logic |
| [Terraform lock](../../.github/workflows/terraform-lock.yml) | Provider lock maintenance |
| [Terraform dev](../../.github/workflows/terraform-dev.yml) | Plan and approved apply; currently destroy mode |
| [Publish setup](../../.github/workflows/publish-database-setup.yml) | Build/publish setup image |
| [Initialize secret](../../.github/workflows/initialize-secret.yml) | Generate/store application credentials |
| [Run setup](../../.github/workflows/run-database-setup.yml) | Launch, wait, verify migration task |

### 9.3 Terraform versions, state, and review cycle

Terraform is pinned to `1.17.2`, AWS provider to `6.73.0`. The S3 backend bucket is `order-management-tfstate-dev-638500336727-us-east-1`, key `terraform.tfstate`, region `us-east-1`, with encryption and `use_lockfile = true`.

1. Check that the bootstrap bucket and GitHub IAM roles still exist before a future rebuild.
2. Open the Terraform workflow. **It currently uses `plan -destroy`.** Remove that option from both planning commands only when intentionally rebuilding.
3. Create a branch, edit the Terraform files, and format from the repository root using the matching installed CLI:

   ```powershell
   terraform -chdir=infra/aws/dev fmt
   terraform -chdir=infra/aws/dev init -backend=false -input=false -lockfile=readonly
   terraform -chdir=infra/aws/dev validate
   ```

4. Save, commit, push, open a pull request, and merge after checks pass.
5. Open **Actions → Terraform AWS Dev → Run workflow → main**. Leave Apply unchecked for preview.
6. Read the complete plan and totals. After destruction, a normal plan proposes recreating infrastructure.
7. For an intended deployment, start a new run with Apply checked. Review its saved plan.
8. Open **Summary → Review deployments → terraform-dev → Approve and deploy**.
9. Read the Apply output. Validation success alone does not mean infrastructure was created.

The saved-plan download must use the producing job's artifact name. Set the download action's `with.name` to `${{ needs.plan.outputs.artifact_name }}`; changing only its display name does not fix rerun mismatches.

### 9.4 GitHub OIDC, roles, and variables

1. Review the IAM GitHub OIDC provider, audience `sts.amazonaws.com`, and trust for the intended repository/branch/environment. The observed token subject included immutable owner/repository IDs; use the actual repository token subject, not a guessed generic example.
2. Keep separate plan, apply, publisher, and migration workflow roles. Do not store long-lived access keys for these jobs.
3. Open **Settings → Secrets and variables → Actions → Variables** and configure actual values:

   | Variable | Value/purpose |
   |---|---|
   | `AWS_REGION` | `us-east-1` |
   | `AWS_TERRAFORM_PLAN_ROLE_ARN` | Plan role ARN |
   | `AWS_TERRAFORM_APPLY_ROLE_ARN` | Apply role ARN |
   | `AWS_ECR_PUBLISH_ROLE_ARN` | Publisher role ARN |
   | `AWS_DB_MIGRATION_ROLE_ARN` | Migration/initializer role ARN |

4. Configure approval on the `terraform-dev` environment as used by these workflows.
5. Keep project permissions scoped where supported. ECS execution roles pull images, retrieve injected secrets, and write logs; task roles govern AWS calls made by application code.
6. If combined inline policies reach the role limit, use a customer-managed project policy. Adding more inline policies does not avoid the combined limit.

### 9.5 VPC, routing, and security groups

The exercise created VPC `10.20.0.0/16` across two Availability Zones: public_a/public_b, app_a/app_b, and database_a/database_b.

1. Use public subnets for the internet-facing ALB and NAT gateway.
2. Use application subnets for Fargate tasks, with public IP assignment disabled and NAT outbound access.
3. Keep RDS in the isolated database subnets.
4. Allow ALB HTTPS `443` from clients, API `8080` only from the ALB security group, and RDS `5432` only from the appropriate task security group.
5. Verify task outbound access to ECR, Secrets Manager, CloudWatch, and PostgreSQL.
6. Obtain new subnet/security group IDs after rebuilding; destroyed IDs cannot be reused.

### 9.6 Private RDS PostgreSQL

The exercise used `order-management-dev-postgres`, PostgreSQL major `18`, `db.t4g.micro`, database `ordermanagement`, administrator `dbadmin`, managed master password, and encrypted 20 GiB gp3 storage.

1. Review database.tf and keep `publicly_accessible = false`.
2. Reference the RDS address from Terraform rather than retaining an old endpoint.
3. Give the setup task the managed administrator secret; do not give it to the API.
4. Use TLS certificate verification. DatabaseSetup uses `SslMode.VerifyFull` and `/app/certs/global-bundle.pem`.
5. Run migrations privately through ECS, rather than opening PostgreSQL to the internet.

### 9.7 ECS foundation

1. Review ecs.tf for cluster `order-management-dev` and log group `/ecs/order-management-dev/database-setup`.
2. Review the execution/task roles and their project permissions.
3. The setup task uses Fargate, Linux/X86_64, `awsvpc`, CPU `256`, and memory `512`.
4. Check that the execution role can read the required secrets and pull the ECR image.
5. A cluster with zero services is expected before API deployment. A one-off setup task is not a continuously running API service.

### 9.8 Publish the database setup image

Scenario: setup source or its Docker image changed.

1. Commit and merge the changes.
2. Open **Actions → Publish Database Setup Image → Run workflow → main**.
3. Wait for a successful build/publish. A failed build has no newly published digest to copy.
4. Open the run **Summary → Database setup image → ECS image**.
5. Copy the entire repository reference including `@sha256:` and its full digest. Do not copy the GitHub artifact digest or only the tag.
6. Replace the quoted `database_setup_image` value in database-task.tf with that complete reference. Do not leave it empty.
7. Format, save, commit, push, and merge.
8. Run a normal reviewed Terraform apply to register a new task revision. Publishing an image alone does not update ECS.

### 9.9 Application secret and login preparation

1. Apply application-secret.tf to create metadata named `order-management/dev/database`.
2. Review initialize-secret.yml. The workflow role needs access to describe/write that secret and generate a random password.
3. Open **Actions → Initialize Secret → Run workflow → main**, then approve if prompted.
4. The initializer stores username `order_app` and a generated 40-character password; it preserves an existing `AWSCURRENT` version.
5. Keep the password out of source, Terraform plaintext inputs, GitHub variables, and logs.
6. Ensure the ECS execution role can read both the administrator and application secrets. The repository statement is named `ReadDatabaseSetupSecrets`; a policy Sid is only a label, not a separate AWS object.
7. Inject `PGUSER`/`PGPASSWORD` from the master secret and `PGAPPUSER`/`PGAPPPASSWORD` from the application secret. Configure `PGHOST`, `PGDATABASE`, and `PGSSLROOTCERT` as task environment values.

Initializing the secret does not create the PostgreSQL login. The setup program does that after migrations.

### 9.10 Task ARN and GitHub variable updates

1. After Terraform succeeds, open **Apply reviewed AWS changes → Apply the saved plan → Outputs**.
2. Copy the complete `database_setup_task_definition_arn`, including the final revision number.
3. Open **Settings → Secrets and variables → Actions → Variables**.
4. Click the pencil beside **DB_SETUP_TASK_DEFINITION_ARN**.
5. Paste the ARN without surrounding quotes and click **Update variable**.
6. Set **DB_SETUP_SUBNET_IDS** to actual application subnet IDs separated by a comma, without spaces.
7. Set **DB_SETUP_SECURITY_GROUP_ID** to the actual task/API security group ID starting `sg-`.
8. If the stored ARN already equals the new output, no update is needed. Update it whenever Terraform registers a revision you intend to run.

### 9.11 Run migrations and configure the application login

1. Open **Actions → Run Database Setup → Run workflow → main**.
2. Approve `terraform-dev` if prompted.
3. Wait for Launch, Wait for task to stop, and Check migration result.
4. Require container exit code `0`. STOPPED is normal for a one-off task.
5. Open the matching CloudWatch stream in `/ecs/order-management-dev/database-setup`.
6. Check `Database migrations completed successfully.` For the newer login code, also require `Application login configured.`
7. The program creates/updates `order_app`, grants connection/schema usage, table CRUD, sequence usage, and default privileges, removes migration-history access, then verifies the application account can connect.
8. Run again for new migrations, login setup changes, or a rebuilt database. Do not repeat merely because a previous run completed.

**Evidence:** setup runs #2 and #3 succeeded; logs showed three pending migrations followed by successful completion. The latest application's login-success message was not supplied, so that specific verification remains open. The cloud database was later destroyed.

### 9.12 Remaining API task and service

These are implementation steps still required after deliberately rebuilding prerequisites.

1. Create a branch for API deployment.
2. Review API Program.cs: it currently reads `ConnectionStrings:DefaultConnection`. Supplying `PGPASSWORD` alone does not append a password to that string.
3. Implement connection-string assembly with `NpgsqlConnectionStringBuilder` using injected application credentials, or inject a complete connection-string secret. Test special-character handling and never log credentials.
4. Include the RDS CA bundle in the API image and enforce certificate verification.
5. Implement a nonsecret `/health` endpoint returning HTTP 200 and test it locally.
6. Create a separate JWT signing secret: at least 32 random bytes encoded as Base64. Inject `Jwt__SigningKeyBase64`; configure issuer, audience, and token lifetime. Keep `BootstrapAdmin__Enabled=false`.
7. Configure trusted reverse-proxy forwarded headers before HTTPS redirection so requests through the ALB are recognized as HTTPS. Do not trust arbitrary forwarded headers from internet clients.
8. Build/test/publish the API image. Obtain its own ECR image digest, not the database setup image.
9. Add an API Fargate task definition, for example in `infra/aws/dev/api-task.tf`: Linux/X86_64, awsvpc, container port 8080, immutable API image, configuration, secret injection, and dedicated CloudWatch logs.
10. Add dedicated API roles, for example in `api-task-iam.tf`. The execution role reads application/JWT secrets, not the database administrator secret.
11. Add `api-service.tf` for the ECS service in both application subnets, public IP disabled, API security group, and initially one desired task for this exercise.
12. Connect its matching container name/port to the ALB target group in 9.13.
13. Format/validate, review a normal plan, merge, and approve apply.
14. Check ECS service events, startup logs, desired/running task count, and healthy targets.

The suggested API filenames above do not exist in the inspected repository; they are remaining additions.

### 9.13 Remaining load balancer, domain, and HTTPS

The user owns a domain, but the exact API hostname has not been supplied.

1. Choose the complete API hostname, such as `api.yourdomain.com` (example only).
2. Request an ACM certificate in us-east-1 and add its DNS-validation CNAME at the authoritative DNS provider.
3. Wait for certificate status Issued.
4. Add Terraform ALB configuration, for example `infra/aws/dev/load-balancer.tf`, using both public subnets and the ALB security group.
5. Create an IP target group for Fargate, HTTP port 8080, health path `/health`.
6. Create HTTPS listener 443 with the certificate, forwarding to the target group.
7. If redirecting HTTP to HTTPS, explicitly add listener 80 and its inbound security-group rule; HTTPS-only rules do not allow port 80.
8. Point the hostname to the ALB with a Route 53 alias or a suitable subdomain CNAME at your DNS provider.
9. Ensure the ECS service depends on the listener/target-group setup.
10. Apply the reviewed plan, wait for healthy targets and DNS, and verify the hostname's certificate.
11. Export the API URL and ALB endpoint as Terraform outputs.

### 9.14 Testing scenarios and acceptance

Run local tests first using [Testing](../../README.md#testing). Public tests require completing 9.12–9.13.

| Scenario | Expected result |
|---|---|
| Setup task on a new database | Migrations and application login succeed; exit 0 |
| Setup repeated without migrations | No pending schema changes; exit 0 |
| GET /health through HTTPS | 200 and healthy target |
| Public product read | Successful response; empty data is valid for an empty database |
| Customer registration/login | Customer account created and JWT issued |
| Protected request without token | 401 |
| Customer performs Admin-only change | 403 |
| Access another customer's order | Denied according to Section 6 ownership behavior |
| Invalid payload | Validation Problem Details |
| Authorized CRUD | Changes persist across requests and task replacement |
| Replace an API task | Service restores desired count and healthy target |
| Isolated bad-credential test revision | Connection/startup fails without password disclosure |

1. Use the payloads and access rules documented in Sections 1–7.
2. Verify the API uses `order_app`, not `dbadmin`, without exposing passwords.
3. Check CloudWatch request correlation and exceptions.
4. Record commit, image digest, task revision, API URL, test date, and outcomes.
5. Do not mark public deployment complete based only on a green Terraform run.

### 9.15 Troubleshooting from this exercise

| Error | Resolution/check |
|---|---|
| fmt exits 3 | Run terraform fmt, save, commit, rerun. Do not guess alignment manually. |
| OIDC assumption denied | Check actual subject, audience, repository, branch/environment trust. |
| Artifact missing on rerun | Download the plan producer's output name; generate a fresh run when needed. |
| RDS delete denied | Apply role needed DeleteDBInstance permission; also confirm deletion is intended. |
| Resource tainted after failed create | Inspect real resource first. If healthy and matching, recover the taint through an authorized state operation and replan instead of blindly replacing. |
| Secret GetResourcePolicy denied | Add project-secret metadata read permission to applicable Terraform policies; distinct from runtime GetSecretValue. |
| Inline policy size exceeded | Use a customer-managed policy; inline limits are combined per role. |
| ECS deregistration denied | Correct the apply role's DeregisterTaskDefinition resource support; exercise used Resource * with regional condition. |
| Invalid sg ID | Replace placeholder text in DB_SETUP_SECURITY_GROUP_ID with a real ID. |
| Duplicate SslMode / CS1912 | Remove duplicate initializer member, rebuild, republish, update task revision. |
| libgssapi_krb5.so.2 message | Earlier migrations completed; newer setup code disables GSS encryption for password authentication. Verify the new image and logs. |
| Cluster has zero services | Expected before API service creation; setup is a one-off task. |
| DisassociateAddress denied on destroy | Fix apply-role disassociate/release permission, then generate a fresh destroy plan. |

### 9.16 Permanent exercise cleanup

The user authorized permanent database and backup deletion. **Destroy affects all resources in this Terraform state, regardless of creation date; it is not an account-wide delete.**

#### Preparation

1. Verify account, region, backend bucket/key, and resource list.
2. Set database cleanup options:

   ```hcl
   deletion_protection      = false
   skip_final_snapshot      = true
   delete_automated_backups = true
   ```

3. Remove the active final_snapshot_identifier setting when skipping the final snapshot.
4. Set ECR `force_delete = true` to delete the repository with exercise images.
5. Format, commit, merge, and apply these changes with a normal plan first. The observed preparation plan was 0 add, 2 change, 0 destroy.
6. Keep the state bucket and workflow roles until cleanup finishes.

#### Destroy workflow

1. Change both workflow planning commands to include `-destroy`:

   ```sh
   terraform plan -destroy -input=false -lock-timeout=5m
   terraform plan -destroy -input=false -lock-timeout=5m -out=dev.tfplan
   ```

2. Keep apply consuming the saved plan:

   ```sh
   terraform apply -input=false -lock-timeout=5m dev.tfplan
   ```

3. Disable post-destroy resource output commands such as ECR repository URL.
4. Commit/merge and run an unchecked-Apply preview. Review the full destroy list.
5. Start a new Apply-enabled run, review its saved plan, and approve terraform-dev.
6. Wait for completion. If partial deletion fails, fix the blocker and start a new run; do not reuse a stale saved plan after state changed.

The first destroy removed the RDS instance and network resources shown in the logs but failed handling the Elastic IP. After the apply role received `ec2:DisassociateAddress` and `ec2:ReleaseAddress`, run #76 completed:

```text
aws_eip.nat: Destruction complete after 1s
Apply complete! Resources: 0 added, 0 changed, 1 destroyed.
```

#### Final audit and remaining manual items

1. Run another destroy preview and confirm no objects remain to destroy. That final empty-plan evidence is still outstanding.
2. In us-east-1 check RDS databases, retained automated backups, and manual snapshots. Manual snapshots are not proven deleted by delete_automated_backups.
3. Check NAT gateways and Elastic IPs. Do not confuse Internet gateways or Route tables with NAT gateways.
4. Leave the other VPC `vpc-079e77f1fa79964e8` and its main route table/internet gateway untouched; they were not the exercise VPC.
5. Check project ECR, ECS running tasks/services, CloudWatch logs, and Secrets Manager. The application secret has a seven-day recovery window, so deletion can be scheduled rather than immediate.
6. Check any ALB, hosted zone, or DNS record only if actually created for the exercise. Do not delete an existing domain or unrelated zone.
7. After confirming empty Terraform state, download the final state for records. If permanently retiring the exercise, empty the exact project S3 bucket including object versions/delete markers, then delete it last.
8. Remove manual project IAM roles/policies only after no workflow needs them. Preserve unrelated roles and any shared OIDC provider.
9. Disable project deployment/publishing workflows or remove their role variables to prevent accidental recreation.
10. Review Billing/Cost Explorer after usage updates; these screenshots do not establish an account-wide zero bill.

Cost checks include RDS, NAT, public IPv4, Fargate, ALB, ECR storage, logs, secrets, backups/snapshots, and S3. A default VPC route table alone does not demonstrate a billed exercise leftover.

### 9.17 End-of-section record

| Milestone | Final evidence/status |
|---|---|
| S3 state, OIDC, approved Terraform | Used successfully |
| ECR setup image publishing | Successful run shown; later build error diagnosed |
| Private network/RDS/ECS setup task | Provisioned, then intentionally destroyed |
| Migrations | Successful runs and CloudWatch completion |
| Application login | Code/secret workflow present; latest login success message unverified |
| API task/service | Remaining |
| ALB/domain/HTTPS | Remaining |
| Public functional tests | Remaining |
| Cleanup | Partial destroy followed by successful final Elastic IP deletion in #76 |
| Zero-resource audit | Empty destroy preview and outside-state audit still needed |

This section is a full exercise guide and completion record. It does not claim the remaining public deployment was completed. Do not run a normal apply after cleanup unless you intend to recreate infrastructure.

[Back to project README](../../README.md)

