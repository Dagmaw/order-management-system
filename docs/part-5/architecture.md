# Current architecture

Recorded 28 November 2026. Terraform and the pinned image values remain the source of truth for a later release.

## Runtime and trust boundaries

```mermaid
flowchart LR
  Client[HTTP client / Swagger UI] -->|Public HTTPS 443| PublicALB[Public API ALB + ACM]
  subgraph VPC[Project VPC]
    PublicALB -->|HTTP 8080 / SG restricted| API[Private .NET Orders task]
    API -->|Private HTTPS 443 / service token| InternalALB[Internal Inventory ALB + ACM]
    InternalALB -->|HTTP 8080 / SG restricted| Inventory[Private Java Inventory task]
    subgraph RDS[One private encrypted PostgreSQL 18 RDS instance]
      OrdersDB[ordermanagement database / order_app]
      InventoryDB[inventory database / inventory_app]
    end
    API -->|PostgreSQL / verified TLS| OrdersDB
    Inventory -->|PostgreSQL / verified TLS| InventoryDB
    Verifier[Approved one-off verifier] -->|GET stock through private HTTPS| InternalALB
    Verifier -->|Public API via outbound route| PublicALB
  end
  API -->|Client credentials over outbound HTTPS| Cognito[Cognito machine identity pool]
  Cognito -->|Short-lived access token| API
  Inventory -->|Issuer / JWKS over outbound HTTPS| Cognito
  Secrets[Secrets Manager] -. ECS execution-role injection .-> API
  Secrets -. ECS execution-role injection .-> Inventory
  API -. Structured logs .-> Logs[CloudWatch log groups]
  Inventory -. Startup / Flyway logs .-> Logs
  Verifier -. Sanitized stock evidence .-> Logs
```

Both application tasks have no public IP. ALB-to-task traffic is restricted by security groups. Managed-service HTTPS/image/log/token access uses the existing outbound/NAT path. Database access is restricted to the intended application/setup security groups. The verifier has HTTPS access and no database rule or secret-read permission.

Customer login uses ASP.NET Core Identity and the API's JWT issuer. Cognito is the separate machine identity for Orders-to-Inventory, not a replacement for Customer login. Product reads are public; product mutation/user administration are privileged; Customers can access only their own orders. Inventory stock changes/reservation operations require their configured scopes; Orders receives reservation scope, not stock administration scope.

## Order submission and recovery

```mermaid
sequenceDiagram
  participant C as Customer
  participant A as Orders API
  participant O as Orders database
  participant I as Java Inventory
  participant D as Inventory database
  C->>A: POST order + Customer JWT + Idempotency-Key
  A->>O: Persist actor/key, request hash and price snapshot
  A->>I: Reserve using submission ID + machine token
  I->>D: Transactional reservation and stock update
  D-->>I: Reserved / insufficient stock
  I-->>A: Reservation result
  A->>O: Complete local order or persist compensation/recovery state
  A-->>C: 201 completed / 202 pending / 409 rejected
  C->>A: Retry same actor, key and payload
  A->>O: Reuse durable submission and completed order
  A-->>C: Same logical result
```

The services do not share a distributed database transaction. Local transactions, durable submission state, idempotent reservation/release, and a background recovery worker handle the boundary. The API recovery worker polls approximately every 10 seconds; leases prevent normal duplicate processing. A changed payload under the same actor/key conflicts. Existing pre-integration orders may have no submission link.

## Delivery and administration boundary

```mermaid
flowchart LR
  Source[Reviewed source] --> CI[.NET / Java / isolated tests]
  CI --> ECR[Published immutable image]
  ECR --> Pin[Reviewed Terraform digest pin]
  Pin --> Plan[Main-only plan / encrypted private S3 / hash]
  Plan --> Approval[terraform-dev reviewer]
  Approval --> Apply[GitHub OIDC Terraform apply role]
  Apply --> Stable[ECS rollout and circuit breaker]
  Stable --> Gate[17 read-only deployment checks]
  Gate --> Evidence[Sanitized GitHub report]
  Approval --> Tasks[Separate setup / migration / verifier workflows]
```

RDS administrator credentials are restricted to controlled setup tasks. The running API and Inventory use their dedicated logins. Execution roles obtain injected secrets; application task roles have no new general AWS data access. The private verifier creates Customer credentials in memory and needs no injected secret.

## Current scope and limits

This is a development learning deployment: one task per service, one shared single-AZ RDS instance with separate logical databases, and no frontend. Seven-day database backups and short log retention are configured. A restore drill, AWS outage/load drill, multi-AZ capacity, full distributed tracing/alerting, end-user federation, Kafka, and Kubernetes are not claimed implemented. The local/CI recovery and concurrency tests and the controlled real AWS stock verification are distinct evidence.
