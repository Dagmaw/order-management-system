# Section 9.2 — AWS Deployment and Verification — 27 November 2026

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
2. Review a normal Terraform plan, then approve application of its saved plan through `terraform-dev`. The current deployment is not in the earlier historical destroy mode.
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

## Detailed walkthrough — 27 November 2026

### 9.2.1 Prepare the Postman AWS environment

1. Open Postman and its environment selector in the upper-right corner.
2. Create or select **Order Management - AWS Dev**. Keep the Local environment separate.
3. Add an enabled variable named `baseUrl` with value `https://api-dev.kaddigitalsolutions.com` and save.
4. Select this environment before running the requests below.
5. Use `{{baseUrl}}` in request URLs. Replace any literal `http://localhost:5080` URL; selecting an environment does not change a hard-coded URL.
6. Save each test as a separate request with a descriptive name. Changing an existing request from POST to GET does not change its saved name automatically.
7. For GET and DELETE requests select **Body → none**, rather than leaving the preceding POST body selected.

### 9.2.2 Publish and register the updated database setup tool

This operation was needed once to add controlled Admin assignment. It is not required for each new account.

1. Update `src/OrderManagement.DatabaseSetup/Program.cs` with the separate `grant-admin` mode. Default mode remains `migrate`.
2. Update `.github/workflows/run-database-setup.yml` with workflow inputs `operation`, `admin_email`, and `admin_user_id`, and JSON ECS container overrides.
3. Commit the changes on a branch, push, open a pull request, and merge into `main` after checks pass.
4. In GitHub choose **Actions → Publish Database Setup Image → Run workflow**.
5. Keep branch `main` and run it. Wait for success.
6. Open the run Summary and copy the complete **ECS image** reference. It must include the repository URL and `@sha256:` digest. Do not use a GitHub artifact hash.
7. In `infra/aws/dev/database-task.tf`, replace `local.database_setup_image` with that reference.
8. Format Terraform, commit, push, and merge after checks pass.
9. Run **Terraform AWS Dev** with apply disabled to inspect the plan. For this image-only change the expected replacement was `aws_ecs_task_definition.database_setup`: 1 add, 0 change, 1 destroy.
10. Confirm the full plan affects only the intended definition. A task definition replacement registers a revision; it does not mean the RDS database is being destroyed.
11. Start an apply-enabled run, review its saved plan, approve `terraform-dev`, and wait for Apply complete.
12. Copy the new `database_setup_task_definition_arn` output.
13. Go to **Settings → Secrets and variables → Actions → Variables** and edit `DB_SETUP_TASK_DEFINITION_ARN`. Paste the complete ARN including its revision, without quotation marks.
14. The revision produced today was `order-management-dev-database-setup:6`. Use newer outputs if the definition changes later.

If the Run Database Setup form only shows a branch selector, its updated inputs are not present in the selected workflow revision. Check `.github/workflows/run-database-setup.yml` on `main` for `workflow_dispatch.inputs`, merge the change, and reopen the form. A workflow-only correction does not require another image build.

### 9.2.3 Register one account and retain its credentials

1. Create a request named **AWS Register Admin Candidate**.
2. Choose **POST** and URL `{{baseUrl}}/api/auth/register`.
3. Choose **Authorization → No Auth**.
4. Choose **Body → raw** and select **JSON** in the format dropdown. This supplies the JSON Content-Type; Text can cause 415 Unsupported Media Type.
5. Enter the following, replacing the email if already used and the password placeholder with your own saved password meeting the API policy:

```json
{
  "name": "AWS Test Admin",
  "email": "aws.admin8@example.com",
  "password": "REPLACE_WITH_YOUR_SAVED_PASSWORD"
}
```

6. Click **Send**. Require 201 Created and record the returned numeric `id` alongside the email. Today's account returned user ID `7`.
7. Do not repeatedly register the same account. Public registration grants Customer regardless of the account name or the Postman request title.
8. Keep the password in your password manager or private local secret storage. No password from the exercise is reproduced here.

### 9.2.4 Grant Admin through the private task

1. In GitHub open **Actions → Run Database Setup → Run workflow**.
2. Choose `main`.
3. Change **Database operation** from `migrate` to `grant-admin`.
4. Enter the exact registered email under **Existing account email**: today's example was `aws.admin8@example.com`.
5. Enter the corresponding returned ID under **Existing numeric user ID**: today's example was `7`. Do not assume the ID matches a number in the email address.
6. Click **Run workflow** and approve the `terraform-dev` environment when prompted.
7. Wait for Launch, Wait for task to stop, and Check migration result. The existing job/step labels can still say migrations even in grant mode.
8. Verify `failures: []`, container name `database-setup`, and `exitCode: 0`. STOPPED and EssentialContainerExited are normal after this one-off program finishes successfully.
9. The program checks email and user ID together, requires the Admin role to exist, and adds membership transactionally. It retains existing Customer membership.
10. Do not enable development bootstrap or change the API environment to Development for this operation.

### 9.2.5 Log in and copy a fresh Admin token

1. Open a separate **AWS login** request.
2. Choose **POST** and `{{baseUrl}}/api/auth/login`.
3. Choose **Authorization → No Auth** and **Body → raw → JSON**.
4. Enter the same email and the exact password saved during registration:

```json
{
  "email": "aws.admin8@example.com",
  "password": "REPLACE_WITH_THE_SAME_SAVED_PASSWORD"
}
```

5. Click **Send**. Require 200 OK, the expected user ID, and `Admin` in `roles`. Both Customer and Admin are expected after adding membership.
6. In the response find `accessToken`. Copy the entire value beginning with `eyJ`, excluding quotation marks. It may wrap over several displayed lines.
7. On protected requests select **Authorization → Bearer Token** and paste into the Token field. Do not prefix the field value with the word Bearer; Postman supplies the header.
8. Tokens are specific to the account that logged in. A Customer token does not become an Admin token by renaming a request or selecting a different environment.
9. Log in again after assigning a role or when the token expires. Update the token on each test request, or reference an appropriately secured variable consistently.
10. Hide tokens/passwords before sharing screenshots. Do not save them in Git or shared collection exports.

### 9.2.6 Verify identity and role permissions

1. Send **GET `{{baseUrl}}/api/auth/me`**, Body none, with the Admin Bearer Token.
2. Require 200 OK, the expected user ID, and Admin in roles.
3. Send **GET `{{baseUrl}}/api/users`** using that same Admin token. Require 200 with a users list.
4. Compare with a Customer-only token: the users endpoint should deny access. Today's Customer request returned 403.
5. A login response containing a token is not the users-list response. Check both the request method and path before interpreting results.

### 9.2.7 Create, retrieve, and update a product

1. Create **AWS Create Product - Admin** with **POST `{{baseUrl}}/api/products`**.
2. Add the fresh Admin Bearer Token. Choose **Body → raw → JSON**:

```json
{
  "name": "AWS Test Product",
  "description": "Product created during AWS deployment verification",
  "price": 25.00
}
```

3. Send and require 201 Created. Record the product ID; today's result was `1`.
4. Send **GET `{{baseUrl}}/api/products/1`**, Body none. Require 200 with the original name and price 25.00.
5. Send **PUT `{{baseUrl}}/api/products/1`** using the Admin token and this JSON:

```json
{
  "name": "AWS Test Product Updated",
  "description": "Updated during AWS deployment verification",
  "price": 30.00
}
```

6. Require 200 with the updated values.
7. Send GET to the same URL, Body none. Confirm the updated name and 30.00 were saved.
8. GET lists/reads products; sending a JSON body with GET does not create a product. Always check the method dropdown.

### 9.2.8 Verify product validation, authorization, and authentication

1. With the Admin token, send **POST `{{baseUrl}}/api/products`** with:

```json
{
  "name": "",
  "description": "Invalid product test",
  "price": -1
}
```

2. Require 400 with errors for required name and minimum price 0.01. This is an intentional test; do not accept Postman's Fix suggestion merely because the response is red.
3. Send **GET `{{baseUrl}}/api/products`**, Body none. Confirm the invalid product was not saved.
4. Log in as a Customer-only account. Today's owner account was `aws.customer1@example.com`, user `2`. Confirm only Customer in roles and copy its token.
5. Send POST products with the Customer token and a valid body:

```json
{
  "name": "Customer Forbidden Test",
  "description": "Customer permission check",
  "price": 10.00
}
```

6. Require 403 Forbidden: authenticated but not permitted.
7. On the same request choose **Authorization → No Auth** and send again. Require 401 Unauthorized: authentication is required.
8. Restore the intended Bearer Token before moving to protected order requests.

### 9.2.9 Create and retrieve a Customer order

1. Log in as the Customer owner and use its token, not the Admin token.
2. Send **POST `{{baseUrl}}/api/orders`**, raw JSON:

```json
{
  "items": [
    { "productId": 1, "quantity": 2 }
  ]
}
```

3. Omit `userId` to use the signed-in owner. Do not send a client-calculated total.
4. Require 201. Today's result: order `1`, owner `2`, unit price 30.00, quantity 2, line total and order total 60.00.
5. Send **GET `{{baseUrl}}/api/orders/1`**, Body none, with the same owner token. Require 200 with the same order.

### 9.2.10 Verify order ownership and Admin access

1. Log in as a different Customer-only account. Today's example was `aws.admin4@example.com`, user `6`; despite its name it was Customer-only. Verify roles rather than inferring from the email.
2. Replace the token on GET orders/1 with that second Customer token. Require access to be denied and record the exact response. Today's denial was confirmed, but its exact status was not supplied.
3. Log in as the approved Admin, copy a fresh token, and repeat GET orders/1. Admin access passed.
4. Return to user 2's Customer token. Send **POST `{{baseUrl}}/api/orders`** with:

```json
{
  "userId": 6,
  "items": [
    { "productId": 1, "quantity": 1 }
  ]
}
```

5. Require 403: user 2 cannot place an order for user 6.

### 9.2.11 Verify rejected order payloads

1. Keep the owner Customer token and POST orders. Send:

```json
{
  "items": [
    { "productId": 1, "quantity": 0 }
  ]
}
```

2. Require 400 because quantity must be at least 1.
3. Change the body to:

```json
{
  "items": [
    { "productId": 999999, "quantity": 1 }
  ]
}
```

4. Require rejection for the missing product and record its status/body. Today's rejection was confirmed without an exact response code.
5. GET orders/1 again with the owner token and Body none. Confirm the original order remains retrievable; its expected quantity is 2 and total 60.00.

### 9.2.12 Verify persistence after replacing the API task

1. In AWS select region **US East (N. Virginia)**.
2. Open **ECS → Clusters → order-management-dev → Services → order-management-dev-api**.
3. Choose **Update service**.
4. Keep the existing task definition and desired task count `1`.
5. Select **Force new deployment** and update the service.
6. Wait for the deployment to complete and the service to settle at 1/1 tasks running. Check health and service events if it does not settle. A rolling deployment may temporarily run an extra task.
7. In Postman obtain a fresh Admin token if necessary.
8. GET products/1 and orders/1 with Body none. Both returned 200 after task replacement, with product price 30.00 and order total 60.00, as confirmed by the user.
9. This demonstrates persistence outside the container. It is not a database backup/restore or regional disaster-recovery test.

### 9.2.13 Verify deletion using a disposable product

1. Retain product 1 because the existing order references it.
2. With the Admin token POST products using:

```json
{
  "name": "Temporary Delete Test",
  "description": "Disposable product for deletion verification",
  "price": 5.00
}
```

3. Record the new returned ID. Do not assume it is 2 or reuse product 1.
4. Send **DELETE `{{baseUrl}}/api/products/RETURNED_ID`**, Admin token, Body none. Replace RETURNED_ID with the actual ID and record the response.
5. Send GET to the same URL. Today's verification returned 404 Not Found. The DELETE response code was not recorded.

### 9.2.14 Finish and preserve the exercise record

1. Save the test requests and record the date, statuses, task revisions, and immutable image references without passwords/tokens.
2. Commit the documentation changes and review them before merging.
3. Keep the detailed walkthrough under docs; the home README should link here and summarize current status.
4. AWS resources remain running. Documentation completion does not stop charges or authorize deletion.
5. Before cleanup, review the full fresh destroy plan and retention choices. Preserve the company domain and hosted zone. Follow the cleanup guidance below.


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

[Back to project README](../../README.md)