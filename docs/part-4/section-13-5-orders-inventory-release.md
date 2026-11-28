# Section 13.5 — Prepare the Orders API release for private Inventory

## Current status

Preparation is merged. [Trusted main-branch CI run 46120120982](https://github.com/Dagmaw/order-management-system/actions/runs/46120120982) passed and published the API candidate from commit `df4dbe4d6ccd466fa85821474edab4709e443fec`. Section 13.6 pins the verified ECR digest and prepares the separate activation review.

Orders migrations completed successfully in AWS in Section 13.4. Java Inventory is running privately and healthy. This section prepares the API image and integration configuration; it does not change the running API image or enable Inventory calls.

## 1. Prepare and verify the API image

The API Dockerfile makes `/app/certs` traversable and verifies that the non-root runtime user can read `/app/certs/global-bundle.pem`. The certificate remains read-only. The same pattern was verified in the Java and DatabaseSetup images. PostgreSQL hostname and certificate verification remain enabled.

A local Linux/amd64 API build passed, including its final runtime-user certificate readability assertion. The image also includes the already tested durable order-submission, recovery, and OAuth adapter code. A local Docker digest is not the deployment pin: the trusted main-branch CI workflow must publish the release image to ECR after merge.

## 2. Disabled-by-default integration settings

`infra/aws/dev/orders-inventory.tf` introduces `orders_inventory_enabled`, default false. While false, its additional environment settings and secret references are empty and no additional API execution-role policy is created. The API task keeps its current image digest.

When a later reviewed release enables the flag, it supplies:

| Setting | Value/source |
| --- | --- |
| Inventory__Enabled | true |
| Inventory__BaseUrl | Private Inventory HTTPS hostname |
| Inventory__TimeoutSeconds | 10 |
| Inventory__AuthenticationMode | ClientCredentials |
| Inventory__TokenEndpoint | Existing Cognito service token endpoint |
| Inventory__ClientId | Existing Orders machine client ID |
| Inventory__Scope | inventory/reserve |
| Inventory__ClientSecret | ECS injection from the dedicated service-identity secret, JSON field clientSecret |

The proposed conditional `api-inventory-identity` policy allows only the API execution role to retrieve the dedicated Orders service-identity secret. The API application task role gets no new AWS permission. The client has no stock.write scope, and the secret is not passed through workflow inputs or retrieved locally.

A task-definition lifecycle precondition blocks enabling integration while the known legacy API digest remains pinned or while no Java image is selected. This guard catches premature activation; it does not replace checking publication, migrations, Java health, and the reviewed plan.

The default preparation plan must report no infrastructure changes. The activation plan is a separate review after the published digest is available.

## 3. Deployment-observation permissions

`infra/iam/api-deployment-read.json` and `scripts/initialize-api-deployment-read.ps1` prepare the API equivalent of the verified Inventory observation fix. The policy includes only ListServiceDeployments, DescribeServiceDeployments, and DescribeServiceRevisions, scoped to the API service and its deployment/revision ARNs. It is intended only for the Terraform apply role.

These API observation permissions have not been installed in this preparation step. Review them with the activation change so the provider can wait for API stability without repeating the earlier Inventory permission failure.

## 4. Publish through the trusted pipeline

1. Review the Dockerfile, disabled integration settings, and proposed scoped permissions.
2. Require the PR's Terraform, .NET, Java, and isolated integration checks to pass.
3. Merge the preparation PR.
4. Wait for the main-branch Build and Test workflow to pass and publish the API image to ECR.
5. Record both the source commit and immutable ECR image digest from its publishing summary. Verify this is an API image, not the DatabaseSetup image in the same repository.
6. Keep the running API digest unchanged during publication.

## 5. Prepare the separate activation review

1. Update `local.api_image` to the new trusted API digest and enable `orders_inventory_enabled` in the reviewed configuration.
2. Confirm Section 13.4 migration success and healthy private Java readiness.
3. Review the conditional execution-role secret permission and the narrowly scoped Terraform apply observation policy. Obtain the necessary deployment/IAM approval before installation or apply.
4. Preview the Terraform plan. Expected resource work is a new API task-definition revision, an in-place API service update, and one additional conditional execution-role policy. Investigate changes outside that scope.
5. Install the approved API observation permissions; deploy through the saved-plan workflow and required terraform-dev approval.
6. Keep both services running and wait for API stability. Existing users, orders, and public login JWT settings are preserved.

## 6. Verify the integrated release

After activation, use a fresh test Customer and a unique Idempotency-Key for an order against a known seeded Inventory product. Verify the returned total, then replay the same key/payload and confirm the same order result. A different payload under the same key must be rejected. Run the existing ownership and authorization checks as well.

Private stock and reservation checks must run from an approved VPC verification path using a narrowly scoped machine identity. Do not open the private service publicly or retrieve live client secrets locally to simplify testing. Public Orders HTTP checks alone do not prove private stock changed exactly once.

The isolated integration suite already covers success, insufficient stock, duplicate submission, concurrent replay, outage, and recovery. Repeat appropriate checks against AWS through a controlled verification workflow after the release. Record actual results; do not mark the AWS integration complete based only on service health.

## 7. Rollout and rollback caution

Keep this preparation flag false until the candidate release is reviewed. Once real orders depend on Inventory, blindly disabling it can bypass stock reservation; it is not an automatic operational rollback procedure. If rollout fails, inspect task logs and dependency readiness before choosing a reviewed recovery action. Leave durable submissions and migration history intact.
