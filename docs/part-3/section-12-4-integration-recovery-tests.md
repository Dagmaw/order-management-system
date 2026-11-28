# Section 12.4 — Automated Integration and Recovery Tests

This section runs the .NET Orders API and Java Inventory service together against two disposable PostgreSQL 18 databases. It extends Section 12.3 with a repeatable HTTP test gate before publishing the API image. AWS deployment remains a separate milestone.

## 1. Prerequisites

Install Docker with the Compose plugin, Python 3.10 or later, and OpenSSL. Linux GitHub runners provide these tools; on Windows the runner also checks Git for Windows' OpenSSL installation. Docker must run Linux containers. Run all commands from the repository root.

No AWS credentials, local environment files, existing databases, or existing JWT keys are required. Internet access is needed to restore packages and pull base images.

## 2. Understand the isolated stack

`compose.e2e.yaml` defines two private PostgreSQL services, an EF migration utility, the API, and Inventory. Each run uses a unique `oms-e2e-` project name. HTTP ports are allocated dynamically and bound to loopback. Database ports are not published.

The runner generates a disposable RSA signing key and independent user-token signing key. Generated signing material is placed under `.ci-integration/`, excluded from Git and Docker build context. Only the API receives the private-key mount; Inventory receives its public key. The setup enables the Development-only Inventory client.

The migration utility builds in an isolated image so Windows build artifacts cannot contaminate Linux restore paths. It restores the tool and application dependencies, then applies the actual EF migrations to the disposable Orders database. Inventory applies its actual Flyway migrations to its own database.

## 3. Run the checks

```powershell
python scripts/e2e-orders.py
```

On Linux, use `python3` if `python` is unavailable. The runner starts the databases, applies migrations, seeds a single disposable product priced at 30.00, builds and starts both applications, and waits for their HTTP readiness.

It registers two temporary Customers through the API and tests:

1. Creating an order for two units returns 201 and a total of 60.00.
2. Repeating its UUID idempotency key returns the same order without another stock deduction.
3. Changing the payload while reusing the key returns 409.
4. Anonymous order reads return 401; a different Customer receives 404 for another Customer's order.
5. Inventory reservation requests without a service token return 401.
6. Insufficient stock returns a compensated 409 and preserves available stock.
7. Four concurrent requests sharing one key complete one logical order and deduct stock once.
8. Stopping Inventory produces a durable Pending submission with 202.
9. A different Customer cannot read that submission.
10. Restarting the API and Inventory allows the recovery worker to complete the pending submission, with one stock deduction.

These checks exercise real HTTP, authentication, PostgreSQL transactions, EF migrations, Flyway migrations, and container restarts. Existing unit and Java concurrency tests continue to run separately.

## 4. Read results and cleanup

Each passing assertion prints a named PASS line. `artifacts/e2e-results.json` records the overall outcome and completed assertions without account passwords or access tokens.

A finally block removes only this run's Compose containers, network, and volumes and deletes its private key. Docker build images/cache can remain. If the process is forcibly terminated, identify the exact `oms-e2e-` project with `docker compose ls` before removing its resources; do not run a global Docker prune.

An assertion failure stops the runner with a nonzero exit code. Check readiness, migration, and build output before changing application behavior. Package-download failures are infrastructure failures, not successful test runs.

## 5. GitHub Actions behavior

The Integration and Recovery job runs for pull requests to main, pushes to main, and manually triggered Build and Test workflows. It uploads the sanitized JSON report even when tests fail, when a report exists.

The ECR publishing job now depends on the .NET tests, Java tests, and this integration job. A failed integration run blocks publishing. This does not deploy an image or change the pinned ECS image automatically.

## 6. Boundaries and next milestone

This suite verifies the local Development-mode service identity and selected recovery paths. It does not prove production identity-provider integration, AWS connectivity, every database failure mode, production load capacity, or operational alerting. Production service identity and private Java deployment remain required before enabling this integration in AWS.

## 7. Verification record — November 27, 2026

The complete runner passed all 20 assertions against disposable PostgreSQL 18 databases and the real API/Inventory containers. This includes concurrent idempotent submissions and recovery after restarting both applications. All test containers, database volumes, and generated private keys were removed afterward. The existing local development services remained running.

The GitHub Actions job is configured but has not yet been run for this change. Push the branch and confirm the Integration and Recovery job succeeds before merging. No AWS deployment was performed.
