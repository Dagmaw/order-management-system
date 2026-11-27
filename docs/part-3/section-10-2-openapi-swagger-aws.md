# Section 10.2 — OpenAPI and Swagger UI on AWS

## Status

Completed and verified on 27 November 2026 through user-provided Terraform output and AWS Swagger screenshots. The local implementation is documented in [Section 10.1](section-10-1-openapi-swagger.md). Documentation is now available over the existing HTTPS AWS hostname; application authentication and role restrictions remain enforced.

This milestone enables documentation for the already deployed .NET Order API. Java Inventory remains local; deploying it to AWS is a separate milestone.

## 1. Scope and access policy

- Keep the API environment Production and `BootstrapAdmin__Enabled=false`.
- Explicitly enable documentation with `ApiDocs__Enabled=true` in the API task environment.
- The documentation pages and JSON document will be publicly readable on the development hostname. Swagger UI is not an authentication boundary.
- Public operations remain public; protected requests still require a valid access token, role, and appropriate ownership.
- Try it out sends real requests to the AWS database. Use deliberate test data and never include passwords, tokens, or secret values in documentation examples or Git.
- Retain current HTTPS, trusted ALB proxy configuration, private tasks, and private database. No new ALB, domain, public database access, or inbound port is required.

## 2. Merge and publish the updated application

1. Create/use a feature branch such as `section-10-2-swagger-aws`.
2. Review the Section 10.1 source changes, tests, documentation rename, README links, and the documentation environment flag in `infra/aws/dev/api-task.tf`.
3. Keep `.env.local` and `.env.inventory.local` excluded from Git. They are only local startup files.
4. Commit, push, open a pull request into main, and merge after CI passes.
5. Open **Actions → Build and Test**, then the run triggered by that main-branch push.
6. Require successful .NET and Java checks and **Publish API image to ECR**. This must be the API image, not the database setup image.
7. Open the run Summary and copy the full **ECS image** reference, including repository URL and `@sha256:` digest.
8. If the publishing job is missing/disabled or fails, resolve that before proceeding; do not substitute an old image without Swagger support.

## 3. Pin the image in Terraform

1. Open `infra/aws/dev/api-task.tf`.
2. Replace only the existing `local.api_image` string with the new full API image reference from step 2.
3. Confirm the environment list contains:

```hcl
{ name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
{ name = "ApiDocs__Enabled", value = "true" },
```

4. Keep the application/JWT secret injection, trusted proxies, port 8080, task roles, and existing bootstrap setting.
5. Run `terraform -chdir=infra/aws/dev fmt` with the repository's pinned Terraform CLI, or let CI verify formatting if the CLI is not installed locally.
6. Commit and merge the image-reference change after checks pass.

Do not replace the database setup image or its workflow task ARN for this deployment. The API and setup images have different entry points and purposes.

## 4. Preview and apply

1. Open **Actions → Terraform AWS Dev → Run workflow**, branch main, with apply disabled.
2. Review the full plan. The expected scope is replacement of `aws_ecs_task_definition.api` and an in-place update to `aws_ecs_service.api` to reference the new revision.
3. Typical totals for this isolated change are **1 add, 1 change, 1 destroy**. Judge the resource details rather than relying only on totals; provider/state differences can change the plan.
4. No database, network, company zone, or load balancer destruction is expected. Investigate any additional changes before approval.
5. Start a new apply-enabled run, review its saved plan, and approve the `terraform-dev` environment.
6. Wait for Apply complete. Record the resulting API task-definition revision and deployed image digest.
7. In ECS, open `order-management-dev` → `order-management-dev-api`. Confirm the deployment completes, desired count remains 1, tasks are healthy, and the primary deployment uses the new revision. A rolling update may briefly run an additional task.

## 5. Verify the documentation through HTTPS

1. Open `https://api-dev.kaddigitalsolutions.com/health`; require 200 and Healthy.
2. Open `https://api-dev.kaddigitalsolutions.com/openapi/v1.json`; require a valid OpenAPI document with title Order Management API and the Bearer security scheme.
3. Open `https://api-dev.kaddigitalsolutions.com/swagger/index.html`; require a rendered UI with the document loaded and existing endpoints listed.
4. If a Servers selector appears, confirm the target is the HTTPS AWS hostname, never localhost or an internal container address. Check generated request URLs before executing.
5. Expand GET products → Try it out → Execute. Confirm the generated request targets the AWS API and returns 200.
6. On a freshly loaded UI with no authorized token, call GET auth/me; require 401.
7. Use POST auth/login with an existing AWS test account and its privately retained password. Do not create another account just to repeat this verification.
8. Copy its accessToken and select Authorize. Paste the token without quotation marks or the Bearer prefix.
9. Call GET auth/me; require 200 and the expected account/roles.
10. With Customer-only credentials, GET users should return 403. With Admin credentials, it should return 200.
11. Log out of the UI authorization dialog/clear the token after testing. Browser authorization is configured not to persist across reloads.
12. Record results, date, image digest, and revision without recording passwords or tokens. Mark this guide complete only after these AWS checks pass.

## 6. Troubleshooting and rollback

| Symptom | Check |
|---|---|
| Swagger/document both 404 | Actual running task image and ApiDocs__Enabled flag; changing local env files does not affect AWS |
| UI loads but document fails | Browser network response for /openapi/v1.json, CloudWatch errors, and deployed image |
| Wrong request scheme/hostname | Trusted forwarded headers and ALB HTTPS setup; do not disable HTTPS to work around it |
| 401 | Fresh token, correct AWS issuer/audience, and Authorize field |
| 403 | Correct account and role; UI naming does not grant Admin |
| Unhealthy deployment | ECS events, task stopped reason, CloudWatch startup logs, and ALB target health |

If documentation must be disabled, change `ApiDocs__Enabled` to `false` and apply a reviewed task/service update. For a faulty application deployment, restore the previously known-good API image reference through a reviewed Terraform change. Keep the current database and secrets; disabling docs requires neither database rollback nor teardown.

## 7. Completion record

- [x] Updated API image published and digest recorded below.
- [x] Terraform apply completed: 1 added, 1 changed, 1 destroyed. The resulting task revision was not supplied; capture it from Terraform outputs/ECS for the audit record.
- [x] Deployed API served HTTPS health successfully and answered requests; separate ECS deployment-status screenshot was not supplied.
- [x] OpenAPI JSON and Swagger UI loaded over the AWS hostname.
- [x] Public request, 401, Customer 403, and Admin access verified through Swagger UI.
- [ ] Results committed without secrets.


### Verified deployment and responses

Pinned API image:

```text
638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:5d7ac5807863bbc0b651561413407ed6a367d3c87e53b299c4aa08b43a0719f6
```

A subsequent image was published by a Terraform-only commit; the intended Swagger-enabled image above remained pinned. Publishing does not require continually switching the deployed digest.

| AWS check | Observed result |
|---|---|
| `/health` | Healthy |
| `/openapi/v1.json` | Document loaded with Bearer security metadata |
| `/swagger/index.html` | UI rendered; HTTPS AWS server selected |
| GET `/api/products`, no token | 200; existing product returned |
| GET `/api/auth/me`, no token | 401 |
| POST `/api/auth/login`, Admin test account | 200; Customer and Admin roles |
| GET `/api/auth/me`, Admin token | 200; expected account and Admin role |
| GET `/api/users`, Admin token | 200; users list |
| GET `/api/users`, Customer token | 403 |

No passwords, access tokens, or credential-bearing Curl examples are retained in this record. AWS resources remain running as requested. Java Inventory deployment and service integration remain future milestones.
[Back to README](../../README.md)
