# Section 12.7 — Amazon Cognito Service Identity in AWS

## 1. Scope and current status

This milestone selects Amazon Cognito for machine-to-machine identity, adds its explicit Inventory token contract, and prepares the AWS configuration and real-provider verification workflow.

Deployed resources are a dedicated service user pool, its OAuth domain, an Inventory resource server, a confidential Orders client, a Secrets Manager secret/version, and a narrowly scoped GitHub verification role/policy. Existing Customer login, ECS API image, RDS databases, DNS, and running services are unchanged.

**Completed and verified.** Cognito resources are deployed, and [verification run 46109337886](https://github.com/Dagmaw/order-management-system/actions/runs/46109337886) passed all 20 assertions using real AWS-issued service tokens. Orders, Inventory, and their disposable PostgreSQL databases ran on the GitHub runner; private Java hosting and integrated Orders deployment in AWS remain the next milestone. The original plan added eight resources without changing or deleting existing infrastructure.

## 2. Cost before deployment

For US East (N. Virginia), AWS currently gives an M2M example priced at $0.00225 per successful token response ($2.25 per 1,000). There is no separate app-client registration charge. One hourly acquisition is approximately $1.62 per 30-day month; refresh headroom, restarts, parallel instances, and testing add requests. The adapter shares a cached token within each API process.

A live verification run normally needs one token before the API restart and another after it, approximately $0.0045 total for two successful responses. Failed retries or extra API instances can change that count. Secrets Manager storage/API calls and existing infrastructure charges are additional. No new continuously running compute service is introduced by the Cognito resources.

Check current [Cognito pricing](https://aws.amazon.com/cognito/pricing/) and [Secrets Manager pricing](https://aws.amazon.com/secrets-manager/pricing/) before applying. The existing daily budget is an alert, not a spending cap.

## 3. Understand the explicit Cognito token contract

Cognito client-credentials grants do not support API resource binding. Its M2M access token identifies the machine with matching `sub` and `client_id`. See [AWS access-token documentation](https://docs.aws.amazon.com/cognito/latest/developerguide/amazon-cognito-user-pools-using-the-access-token.html) and [resource-binding limitations](https://docs.aws.amazon.com/cognito/latest/developerguide/cognito-user-pools-define-resource-servers.html#cognito-user-pools-resource-binding).

`INVENTORY_JWT_MODE=Cognito` therefore requires:

1. An RS256 signature verified using the exact pool's HTTPS JWKS endpoint.
2. Exact configured pool issuer, required expiration, nonempty subject, and normal timestamp validation.
3. An allowlisted string `client_id`.
4. `sub` exactly equal to that client ID, excluding human-user tokens.
5. `token_use=access`, rejecting ID tokens and missing token-use claims.
6. The configured reservation or stock permission scope.
7. The optional `typ` header may be absent, as in AWS Cognito access tokens. If supplied, only `JWT` or `at+jwt` is accepted. Generic provider mode still requires its explicit type contract.

Only this explicit mode uses Cognito's machine-client contract instead of an API audience. Generic `Jwks` mode continues to require the configured audience, including when a token omits `aud`. Missing audiences now return a validation failure rather than an unchecked null error.

The Cognito issuer must match a standard AWS-region user-pool issuer, and the JWKS URL must equal that issuer plus `/.well-known/jwks.json`. This implementation targets the standard AWS partition used by this project.

## 4. Review Terraform and least privilege

Read `infra/aws/dev/service-identity.tf` before deployment:

- Pool: `order-management-dev-services`, Lite tier, deletion protection enabled, public self-registration disabled.
- Domain prefix: `oms-dev-services-638500336727`.
- Resource-server scopes: `inventory/reserve` and `inventory/stock.write`.
- Orders client: only the OAuth `client_credentials` grant and `inventory/reserve` permission. No stock-administration scope is granted.
- Token lifetime: one hour.
- Secret: `order-management/dev/orders-service-identity`.
- Verification role: `order-management-dev-service-identity-test`, assuming the existing repository's `terraform-dev` environment through GitHub OIDC, with read access to only the dedicated secret.

The Terraform output `cognito_service_identity` contains non-secret connection information. It never outputs the client secret. Terraform must retain generated client credentials in its protected state; do not publish state or binary plan files.

The supplementary role policies are reviewable at:

- `infra/iam/cognito-terraform-plan.json`: scoped Cognito configuration reads, the dedicated secret/verification-role reads, and private saved-plan upload/read.
- `infra/iam/cognito-terraform-apply.json`: those reads plus tagged project-pool management, the dedicated secret, only the verification IAM role, and private saved-plan download/deletion.

The bootstrap attaches one named managed policy to each existing Terraform role. It does not grant AdministratorAccess or alter unrelated roles. It verifies account 638500336727 and fails rather than overwriting an existing policy with different contents.

## 5. Approve and bootstrap the required IAM permissions

Automatic approval review initially rejected the bootstrap because persistent IAM grants needed explicit approval. The user subsequently approved the IAM changes, and both policies are now attached. The plan role also requires `s3:GetBucketPublicAccessBlock` on the existing state bucket to perform the workflow privacy check; this read-only correction is included in the policy file.

After reviewing and approving the exact policy files, run from the repository root with the existing administrative learning profile:

```powershell
./scripts/initialize-cognito-permissions.ps1 -Profile order-management-dev
```

The script uses the pinned official AWS CLI image and mounts the local credentials file read-only. It creates/attaches the two scoped policies to `order-management-dev-terraform-plan` and `order-management-dev-terraform-apply`. Do not replace this with broader policies if a command fails; inspect the specific denial.

Because these Terraform roles are provisioned outside the application Terraform state, this prerequisite is an explicit bootstrap step rather than a circular dependency in the deployment itself.

## 6. Preserve private saved-plan review

This repository is public. Saved Terraform plans can contain sensitive values even when terminal output masks them, and authenticated readers can download public-repository Actions artifacts. See [Terraform saved-plan guidance](https://developer.hashicorp.com/terraform/cli/commands/plan) and [GitHub artifact access](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).

The Terraform workflow now stores the binary plan under the existing private state bucket:

`reviewed-plans/<run-id>/<attempt>/<commit>.tfplan`

It verifies all four bucket public-access blocks are enabled, requests AES256 server-side encryption, and passes only the object key and SHA256 digest between jobs. The approved apply job downloads that exact object and verifies its digest before applying it. A successful apply removes the saved object; failed or abandoned reviews leave it private for operator review/cleanup. Do not delete the state file or its versions when cleaning up saved plans.

Terraform runtime files, state, and saved plans are also excluded from the API Docker build context.

## 7. Commit, review, and merge

1. Commit Section 12.7 on its feature branch and open a PR to main.
2. Confirm Build and Test passes, including the disposable integration suite.
3. Confirm Terraform formatting, initialization, and validation pass.
4. Review the IAM bootstrap policies and private-plan workflow change along with the Cognito token contract.
5. Merge only after the checks and review complete.

A feature-branch local plan is evidence of the proposed resource changes, not a substitute for the exact main-branch saved plan used by the approved workflow.

## 8. Run the approved AWS deployment

1. Open GitHub Actions → **Terraform AWS Dev** → **Run workflow** on `main`.
2. Run with `apply=false` first and review the preview. Expect the eight additions; investigate changes to existing resources or any deletion.
3. Run again with `apply=true`. The plan job saves a fresh private plan in S3.
4. Review the plan output before approving the `terraform-dev` environment apply job.
5. Confirm `Apply complete` and the non-secret `cognito_service_identity` output.
6. If an IAM action is denied, stop and review the scoped permission needed. Do not expand permissions indiscriminately.

This deploys identity resources only; it does not enable Inventory in the running AWS API or deploy Java.

## 9. Verify real Cognito-issued tokens

After the Terraform apply succeeds:

1. Open GitHub Actions → **Verify Cognito Service Identity**.
2. Run it on `main` and approve its `terraform-dev` environment job.
3. The workflow assumes the secret-read-only verification role.
4. `scripts/verify-cognito.py` reads the dedicated secret into process memory without printing it and configures the disposable Compose stack for ClientCredentials/Cognito mode.
5. The real .NET adapter requests a token from Cognito; the real Java decoder retrieves the pool JWKS and validates that token.
6. The same 20 integration assertions verify order creation, replay, permissions, insufficient stock, concurrent requests, and recovery across restarts.
7. Inspect the sanitized JSON results artifact and require a successful workflow conclusion.

The application containers and their PostgreSQL databases run on the temporary GitHub runner. Cognito and Secrets Manager are real AWS services. The test cleans up only its disposable containers/databases; it keeps the Cognito resources for continued project work. No tokens, client secrets, or raw Terraform plans are uploaded as test artifacts.

The verification workflow is manual and main-branch-only so ordinary pull requests do not issue billable Cognito requests or receive AWS secret access.

## 10. Next AWS integration milestone

After real-provider verification passes, deploy Java privately with its own database credentials, production profile, HTTPS connectivity, health checks, logs, and network rules. Inject the Orders client secret into the future API task through Secrets Manager and grant only the necessary role access. Review/apply Orders migrations before enabling integration. Keep the current AWS API image pinned until that reviewed deployment is ready.

## 11. Verification record — November 27, 2026

- Terraform formatting and validation passed using the locked AWS provider 6.73.0 and Terraform 1.17.2.
- A live read-only plan against the existing backend reported 8 additions, 0 changes, 0 deletions.
- All 33 Java tests passed, including the explicit Cognito contract and unchanged generic audience enforcement.
- All 20 local two-service integration assertions passed after updating optional provider settings and Docker exclusions.
- AWS IAM Access Analyzer reported no policy-validation errors for either proposed bootstrap policy. The PowerShell bootstrap and Python verification runner passed syntax checks.
- The existing state bucket was verified to have all four public-access blocks enabled.
- Initial preparation applied no Cognito resources or IAM grants. After explicit user approval, the IAM bootstrap was installed and the main-branch plan passed. The initial apply required the recovery documented below. Deployment and real-token verification subsequently completed; see the completion record in section 15.

## 12. Recovery after the first apply

The initial approved apply stopped when Terraform tried to read the dedicated secret's resource policy. The existing secret read statement now includes `secretsmanager:GetResourcePolicy` in both approved Terraform-role policies, restricted to the same dedicated secret ARN pattern.

The failure left the secret in state with a taint marker despite successful creation. Its AWS metadata and Terraform attributes were verified, then only that marker was cleared with `terraform untaint aws_secretsmanager_secret.orders_service_identity`. No secret was deleted or recreated. The intermediate recovery run was cancelled before applying its stale plan, and a fresh saved-plan run was started from repaired state.

Do not rerun the original failed apply job: its binary plan is stale after the partial apply and state repair. Review the new plan and require no deletions or replacements of the already-created resources. The expected remaining additions are the secret version and verification role's inline policy.

## 13. GitHub OIDC subject correction

The first verification run could not assume its secret-read-only role because the trust policy used the legacy repository-name-only subject. This repository uses immutable owner/repository IDs, as confirmed by its working Terraform role trust policy:

`repo:Dagmaw@31310623/order-management-system@1431786532:environment:terraform-dev`

The verification role and its Terraform source now require that exact subject and `aud=sts.amazonaws.com`. No wildcard was added and its dedicated-secret read policy was unchanged. See [GitHub's AWS OIDC guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-aws).

The Cognito recovery apply completed successfully in run 46107174616: 2 additions, no changes or deletions. The subsequent header correction and successful real-provider verification are documented below.

## 14. Cognito access-token header compatibility

The first real-provider verification passed GitHub role assumption and application registration/login, then failed its first order assertion. Local Cognito test fixtures had always included `typ=JWT`, but AWS documents Cognito headers with only `kid` and `alg`. Reproducing that documented header caused the existing Inventory validator to reject an otherwise valid Cognito M2M token.

The regression fixture now omits `typ`. The validator accepts an absent type only in explicit Cognito mode while preserving signature, pool issuer, machine-subject/client equality, allowlist, token-use, expiration, and scope checks. Unsupported explicit types remain rejected. A separate regression test confirms generic mode still rejects a missing type even when its audience matches.

The integration runner now reports only the first order's HTTP status and submission state on assertion failure. It never prints the token, client secret, or full response body. The local diagnostic route that would access live credentials outside the approved verification role was rejected by automatic review and was not executed. Live credential access remains inside the approved GitHub workflow.

After merging this fix, start a new **Verify Cognito Service Identity** run on main. Rerunning the old failed run would use its old commit and repeat the old behavior. Require the full real-provider test pass before marking service-token exchange verified.

Reference: [AWS access-token header documentation](https://docs.aws.amazon.com/cognito/latest/developerguide/amazon-cognito-user-pools-using-the-access-token.html).

Verification of the header fix: all 34 Java tests passed locally and in the Linux image build, and all 20 disposable two-service integration assertions passed. The subsequent fresh main-branch run passed with real Cognito tokens; see section 15.

## 15. Completion record — November 27, 2026

[Verify Cognito Service Identity run 46109337886](https://github.com/Dagmaw/order-management-system/actions/runs/46109337886) completed successfully on merged commit `4104bf53a3d97103693b46906a3e7ed68343c5ff`.

All 20 assertions passed with the real Cognito token endpoint and pool JWKS, including:

- Customer registration/login and an order totaling 60.00 for two units priced at 30.00.
- Replay returning the same order without a second stock reservation; changed payload returning a conflict.
- Anonymous and cross-customer access rejection.
- Inventory mutation requiring a service token.
- Insufficient-stock compensation preserving stock.
- Concurrent same-key requests reserving only once.
- Inventory outage leaving a durable Pending submission.
- Recovery after API and Inventory restarts completing one order with exactly one stock deduction.

The verification role assumed successfully through GitHub OIDC, read only the dedicated client secret, and the workflow uploaded its sanitized results artifact. No access token or client secret was printed. The test used temporary application containers and databases on the GitHub runner, and its cleanup completed.

Cognito service identity and real-token interoperability are complete. The deployed AWS API remains on its existing image and existing Customer login. Java Inventory has not yet been deployed into the AWS application network; that deployment, production HTTPS connectivity, API secret injection, database migrations, and integrated AWS smoke checks remain next.
