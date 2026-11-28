# Section 13.8 — Credential-free private stock verifier

## Status

The verifier implementation, non-root image, isolated integration coverage, and manual trusted publishing workflow are prepared. AWS private verification has not run yet. No AWS task, IAM, security group, or live application configuration is changed by this implementation step.

## 1. Verification design

The private stock lookup is readable without an access token inside the VPC. The verifier therefore needs no Cognito client secret, database password, database connection, or machine-token minting. It uses fresh Customer credentials created in memory through the public API, just as a client would.

All Inventory access from the verifier is GET `/api/inventory/1`. Stock-changing requests go through the real Orders API and its already configured machine identity. The verifier does not reserve/release directly or reset stock.

The accepted order remains a real development test order with its two units reserved. Its Customer and compensated submission remain as audit data. Do not release the accepted reservation independently from the order merely to restore a test fixture.

## 2. Checks performed

1. Require healthy public API and existing Orders product 1.
2. Read private stock quantity and version.
3. Require a bounded development fixture with 2–999 units available, so a two-unit success and a within-limit rejection are both possible.
4. Register and log in one fresh test Customer. Password and token remain only in memory.
5. Require the API to reject an order without an idempotency key.
6. Read stock again and require it unchanged during setup.
7. Create a two-unit order with a UUID idempotency key and verify the frozen price.
8. Require quantity to decrease by exactly two and stock version to increase by exactly one.
9. Replay the same key/payload; require the same order and no quantity/version change.
10. Reuse that key with a different payload; require 409 and no quantity/version change.
11. Submit a separate request for current available quantity plus one, within the DTO maximum of 1,000. Require 409 Compensated and no stock/version change.
12. Require exactly one accepted order for the fresh Customer.

These checks cover 22 assertions including lookup and registration prerequisites. They verify stock deltas and public idempotency, not direct database reservation-row inspection. Other concurrent stock activity makes exact deltas inconclusive; the verifier fails rather than attributing unrelated writes to this order. Use a quiet controlled development test window.

## 3. Isolated verification evidence

The existing disposable PostgreSQL/.NET/Java integration runner now invokes this verifier after its recovery checks. The local run passed the existing checks and all 22 verifier assertions:

| Snapshot | Quantity | Version |
| --- | --- | --- |
| Before order | 94 | 3 |
| After accepted two-unit order | 92 | 4 |
| After replay | 92 | 4 |
| After payload conflict | 92 | 4 |
| After insufficient-stock rejection | 92 | 4 |

The test fixture was disposable and removed afterward. Existing local project containers and AWS services were not stopped or changed. This evidence is isolated test evidence, not a completed AWS private run.

Run it from the repository root with Docker Desktop/Linux containers:

```text
python scripts/e2e-orders.py
```

The sanitized `artifacts/e2e-results.json` now includes the private verifier report and stock snapshots. The verifier accepts loopback HTTP only through its explicit local test interface; its normal AWS mode fixes the exact project HTTPS origins. Redirects are rejected, and TLS verification stays enabled.

## 4. Build and publish the verifier

The image uses `scripts/Dockerfile.private-verifier` and contains only the Python standard-library verification script. It runs as UID/GID 65532 and checks that the trusted CA bundle is available to that user. A local Linux/amd64 build passed.

1. Review and merge the implementation after CI passes.
2. Start **Publish Private Stock Verifier Image** on main.
3. The workflow runs the isolated integration/verifier checks before assuming the publishing role.
4. It builds the verifier image and publishes it to the existing project API ECR repository with a distinct immutable `private-verifier-<sha>-<run>-<attempt>` tag.
5. Record the resulting digest. This is a verifier image, not an API or DatabaseSetup image; never assign it to either running application task.

The publishing workflow does not run live AWS verification or retrieve service secrets.

## 5. Prepare the private AWS task after publication

The following infrastructure is the next reviewable step, not yet deployed by this implementation PR:

- A one-off Fargate task pinned to the published verifier digest. No continuously running service.
- A dedicated execution role limited to ECR pull and the verifier log group; no GetSecretValue permission.
- An application task role with no AWS permission policy.
- A dedicated verification security group with outbound HTTPS and narrowly scoped ingress permission on the private Inventory ALB. No public IP, public Inventory ingress, or database access rule.
- A main-only launch workflow behind `terraform-dev`, with a narrowly scoped workflow role that can launch only the fixed verification task and inspect its result. No arbitrary credential, command, or task overrides.

Review that exact task/IAM/network plan before installing or applying it. Then approve the one-off launch through GitHub. It creates one fresh Customer and one accepted two-unit development order plus the rejected submission. It incurs only one-off task and associated usage costs, not another steady-state service.

## 6. Record actual AWS evidence

Require task exit 0 and a sanitized report showing AWS-private mode, the accepted order ID, before/after quantity/version, unchanged replay/conflict/rejection snapshots, and all checks passing. Keep passwords, customer tokens, machine credentials, and raw HTTP response bodies out of logs/artifacts.

No outage drill, direct reservation-table inspection, concurrent load testing, shared-stock reset, or service restart is included. Those require their own controlled procedures if pursued later.

## 7. Maven bootstrap throttling correction

The first PR CI attempt failed before the verifier ran: Maven Central returned HTTP 429 while the Inventory Docker build downloaded the Maven 3.10.0 distribution. The Python CalledProcessError was only the Docker Compose wrapper error.

`services/inventory-service/bootstrap-maven.sh` now runs only Maven's version/bootstrap command with at most four attempts, waiting 10/20/40 seconds for recognized transient transport failures. Other bootstrap failures fail immediately. After bootstrap, the actual `clean verify` runs once; test failures are not retried or skipped. Docker builds reuse a BuildKit Maven-directory cache, and standalone Java CI uses the same bootstrap helper.

Four offline container checks cover successful bootstrap, recovery after two 429s, immediate checksum failure, and exhaustion after four 429s. All passed. The corrected Inventory Docker image also built successfully with its full Java test suite. This correction changes build behavior only, not the running AWS image pins or application behavior.
