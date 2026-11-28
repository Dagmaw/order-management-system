# Section 13.2 — Private Inventory database bootstrap

## Current status

Section 13.1 is deployed. The private endpoint, secret metadata, credentials, and Inventory image exist. The Java service is not running in AWS yet. This section prepares database bootstrap code and isolated tests; it does not apply infrastructure or run against RDS.

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
