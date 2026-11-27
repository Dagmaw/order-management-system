# Section 9.1 — AWS Deployment and Verification

For the detailed walkthrough of today's work, see [Section 9.2](section-09-2-aws-deployment.md). This guide retains the deployment overview, procedures, and verification record.

## Status as of 27 November 2026

The Order Management API was rebuilt, deployed over HTTPS, and verified through Postman. This record is based on workflow logs, screenshots, and user confirmations. Resources remain running; cleanup of the rebuilt environment has not occurred.

The previous environment was destroyed to save costs earlier in November. That historical cleanup does not describe the current deployment. Inventory remains a separate local service and was not deployed or integrated with Orders in this exercise.

## Architecture

```text
Client → Route 53 → public ALB (HTTPS) → private ECS Fargate API → private RDS
GitHub Actions → OIDC → Terraform / ECR publishing / approved private setup task
Secrets Manager → ECS secret injection
CloudWatch ← API and setup logs
```

- API: `https://api-dev.kaddigitalsolutions.com`; region `us-east-1`.
- Company domain: `kaddigitalsolutions.com`; hosted zone `Z09662321NARFICJD1XGZ`.
- ACM certificate validated through DNS; HTTP redirects to HTTPS. ALB targets use HTTP port 8080 and `/health`.
- ALB spans two public subnets. Tasks use private application subnets with public IPs disabled and NAT outbound access. RDS uses isolated database subnets.
- Security groups restrict API ingress to the ALB and database traffic to the task security group.
- RDS uses encrypted storage and TLS certificate verification. The API uses `order_app`; administrator credentials are reserved for setup.
- API runs in Production with trusted proxy configuration and `BootstrapAdmin__Enabled=false`.
- One desired Fargate API task is an exercise cost choice, not a highly available production configuration.

## Implementation and automation

| Source | Responsibility |
|---|---|
| [AWS Terraform](../../infra/aws/dev/) | VPC, subnets, routing, private RDS, ECS, secrets, ACM, ALB, and DNS |
| [API task](../../infra/aws/dev/api-task.tf) and [IAM](../../infra/aws/dev/api-task-iam.tf) | Immutable image, application configuration, secret injection, runtime permissions |
| [API service](../../infra/aws/dev/api-service.tf) and [ALB](../../infra/aws/dev/load-balancer.tf) | Private running tasks, target group, listeners, health checks |
| [Certificate](../../infra/aws/dev/api-certificate.tf) | ACM and DNS validation |
| [Setup program](../../src/OrderManagement.DatabaseSetup/Program.cs) | Migrations, application-login setup, controlled Admin assignment |
| [Setup definition](../../infra/aws/dev/database-task.tf) | Setup image digest, secret injection, task revision |
| [API startup](../../src/OrderManagement.API/Program.cs) | Health, database TLS, proxy trust, Identity/JWT |
| [CI](../../.github/workflows/ci.yml) | .NET/Java checks and API publishing |
| [Terraform workflow](../../.github/workflows/terraform-dev.yml) | Plan and approved saved-plan apply |
| [Publish setup](../../.github/workflows/publish-database-setup.yml) | Setup image publishing |
| [Run setup](../../.github/workflows/run-database-setup.yml) | Approved one-off private task and exit-code verification |
| [Initialize application secret](../../.github/workflows/initialize-secret.yml) | Database credential initialization |

Use separate GitHub OIDC roles for plan, apply, publishing, and setup. No long-lived access keys are needed. The encrypted S3 backend uses locking; preserve it until any cleanup is complete. The exercise used Terraform 1.17.2 and AWS provider 6.73.0.

Deployment sequence:

1. Change source on a branch, format/validate Terraform, review, and merge after CI passes.
2. Review a normal Terraform plan, then approve application of its saved plan through `terraform-dev`. The workflow was restored to normal planning for this rebuild; check the current workflow and full plan before running it.
3. Publish images and use the complete ECR `repository@sha256:digest` output. Publishing alone does not deploy ECS.
4. Apply task-definition changes and update workflow variables from current outputs, including revision numbers, application subnet IDs, and security group ID.
5. Initialize database and JWT secrets through their workflows. Existing current values are preserved; keep values out of Git, Terraform plaintext, and logs.
6. Run migration setup privately. Require migration completion, application-login configuration, and exit code 0.
7. Deploy API/ALB/DNS/certificate, wait for a completed service deployment and healthy targets, and verify HTTPS `/health`.

The setup task uses administrator credentials for schema/login operations; the API execution role reads only its application/JWT secrets. Secret initialization alone does not create the database login.

## Controlled Admin grant

Public registration creates Customer accounts. Naming an account Admin does not grant permissions.

1. Register the account and save its password and returned numeric user ID.
2. Set `DB_SETUP_TASK_DEFINITION_ARN` to a revision containing the grant operation.
3. In **Actions → Run Database Setup → Run workflow**, choose `main` and `grant-admin`.
4. Supply the existing email and matching numeric user ID. The program checks both and adds the existing Admin role in a transaction. Repeating an existing grant is safe.
5. Approve the environment and require exit code 0. STOPPED is normal for this one-off task.
6. Log in again and verify Admin in the fresh token through `/api/auth/me`. Previously issued tokens do not gain new claims.

Default `migrate` mode remains separate. Do not enable development bootstrap on the production API for role assignment.

Latest confirmed setup definition: `order-management-dev-database-setup:6`.

```text
638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:a3050417bfcb1740fb1b897ebb99cad14c85d3bb5f344e1d29168eaf1b3c78b8
```

This is a milestone reference; use current outputs for subsequent runs.

## Public verification results

Postman environment: **Order Management - AWS Dev**, with `baseUrl=https://api-dev.kaddigitalsolutions.com`. Reference `{{baseUrl}}` in requests. A literal localhost URL ignores the selected environment. Do not export passwords or tokens with collections.

| Scenario | Recorded result |
|---|---|
| HTTPS health | Healthy |
| Customer registration | 201 |
| Login and current account | 200 with expected account/role claims |
| Customer reads Admin-only users endpoint | 403 |
| Admin reads users | 200 |
| Admin creates product | 201; product 1 at 25.00 |
| Retrieve product | 200 |
| Update product and read again | 200; updated name and price 30.00 retained |
| Empty name / negative price | 400 field validation errors |
| Product list after invalid create | Only valid product present |
| Customer creates product | 403 |
| Anonymous creates product | 401 |
| Customer creates own order | 201; order 1, owner 2, quantity 2, unit price 30.00, total 60.00 |
| Owner reads order | 200 |
| Another Customer reads order | Denied; exact status not recorded |
| Admin reads Customer order | Passed, confirmed by user |
| Customer creates order for another user | 403 |
| Quantity zero | 400 |
| Missing product in order | Rejected; exact status not recorded |
| Original order after invalid requests | 200 |
| Force new API deployment and read data | Both reads 200; product price 30.00 and order total 60.00 retained, confirmed by user |
| Disposable product deleted | Subsequent GET 404; DELETE response code not recorded |

Product 1 remains because order 1 references it. Deletion used a separate disposable product. Order totals are calculated by the server from product prices; Customer requests omit `userId` to use the signed-in owner.

These are functional exercise results, not a load test, security audit, disaster-recovery test, or proof of production availability. Automated public smoke tests and bad-credential fault injection were not recorded.

## Troubleshooting lessons

| Symptom | Check / resolution |
|---|---|
| Terraform fmt exits 3 | Run the matching formatter, save, commit, rerun |
| Old VPC/subnet/EIP IDs after rebuild | Update scoped IAM permissions and workflow variables using actual outputs |
| NAT creation denied | Review permission for the selected subnet, EIP, and NAT resource |
| OIDC denied | Match actual repository/branch/environment subject and audience |
| Inline policy size exceeded | Use scoped customer-managed policies; multiple inline policies share the role limit |
| ECS ListServiceDeployments denied | Add supported deployment read permissions to the applicable Terraform roles |
| Tainted service after failed waiter | Inspect actual service health and configuration before any controlled state recovery, then replan |
| Missing plan artifact on rerun | Download the producer job's output artifact name |
| No workflow operation fields | Merge updated workflow into main and reopen Run workflow |
| 403 after Admin grant | Log in again and check the token on that specific request |
| 415 on registration | Choose raw JSON and correct Content-Type |
| Localhost connection refused | Use the AWS base URL |
| Setup task STOPPED | Exit code 0 indicates successful one-off completion |

## Costs and future cleanup

Resources remain running. RDS, NAT, public IPv4, ALB, Fargate, secrets, storage, logs, and backups can incur charges. No account-wide zero-cost claim is made.

Before destructive cleanup:

1. Decide whether database contents must be retained and review snapshots, backups, and deletion protection. Exercise settings may permit deletion without a final snapshot.
2. Verify account, region, backend, and the entire fresh destroy plan. Obtain approval for the actual deletion scope; never reuse a stale plan after state changes.
3. **Preserve the company domain and hosted zone for kaddigitalsolutions.com.** Review removal of exercise API/validation records separately and preserve unrelated DNS.
4. Leave unrelated/default VPC resources untouched. Terraform destroy affects the selected state, not the whole account.
5. Keep state and workflow IAM roles until cleanup/audit finish. Check RDS backups/snapshots, NAT/EIP, ECS, ECR, secrets, logs, and outside-state resources.
6. Confirm an empty destroy preview for retired exercise infrastructure and record deliberate retained resources. Review billing after usage updates.
7. Disable publishing/deployment when retiring the exercise. Remove the temporary recovery workflow once no longer needed.

Cleanup of the rebuilt environment has not been performed or authorized by this documentation update.

[Continue to Section 9.2 — Detailed walkthrough](section-09-2-aws-deployment.md)

[Back to project README](../../README.md)