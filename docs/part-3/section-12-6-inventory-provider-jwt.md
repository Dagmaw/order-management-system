# Section 12.6 — Inventory Identity-Provider JWT Validation

## 1. Purpose and milestone boundary

Section 12.5 added the Orders-side OAuth client-credentials adapter. This section adds the Java resource-server side: Inventory can validate access tokens signed by an identity provider using its explicit HTTPS JSON Web Key Set (JWKS) endpoint.

Both application adapters now exist. A real provider tenant/client, end-to-end provider token exchange, and AWS deployment remain separate work. No AWS resources or running task definitions are changed in this section.

The implementation uses Spring Security's supported Nimbus JWT decoder and issuer validators. See the [Spring JWT resource-server documentation](https://docs.spring.io/spring-security/reference/servlet/oauth2/resource-server/jwt.html).

## 2. Review authentication modes

| Mode | Trust source | Use |
| --- | --- | --- |
| `LocalRsa` | Existing Base64 DER public key | Existing local development and disposable integration tests |
| `Jwks` | Explicit HTTPS issuer, JWKS URL, audience, and service-client allowlist | Identity-provider integration |

Local development keeps its existing default. Activating the Spring `production` profile defaults the JWT mode to `Jwks`. Explicit `LocalRsa` is rejected under either `production` or `prod`. Set `SPRING_PROFILES_ACTIVE=production` in the future ECS task; do not omit the environment profile as a substitute for production configuration.

Provider mode does not fall back to the local key if settings or token validation fail.

## 3. Configure provider trust

Configure these variables through deployment settings; the example names below are placeholders, not a provisioned authorization server:

| Variable | Purpose |
| --- | --- |
| `SPRING_PROFILES_ACTIVE` | `production` for AWS |
| `INVENTORY_JWT_MODE` | `Jwks` |
| `INVENTORY_JWT_ISSUER` | Exact HTTPS issuer expected in `iss` |
| `INVENTORY_JWT_JWK_SET_URI` | Provider's explicit HTTPS public signing-key endpoint |
| `INVENTORY_JWT_AUDIENCE` | Inventory API audience expected in `aud` |
| `INVENTORY_JWT_ALLOWED_CLIENT_IDS` | Comma-separated IDs of permitted confidential service clients |
| `INVENTORY_JWT_RESERVATION_SCOPE` | Reservation permission, default `inventory.reserve` |
| `INVENTORY_JWT_STOCK_SCOPE` | Stock-administration permission, default `inventory.stock.write` |
| `INVENTORY_OPERATIONS_ENABLED` | `true` when enabling the operations |

Issuer and JWKS URLs must use HTTPS and cannot contain embedded credentials, query strings, or fragments. The service-client allowlist cannot be empty. Reservation and stock scopes must be distinct, nonempty single scope names.

Inventory needs only public verification keys. Do not put the Orders client secret or a JWT private signing key in the Inventory task. The Orders secret belongs in its secret-store injection configured in the later deployment milestone.

## 4. Agree on the token contract

The authorization server must issue RS256-signed access tokens with:

1. The configured `iss` and Inventory `aud`.
2. A nonempty `sub` and a required `exp`; normal expiration and `nbf` checks apply through Spring validators, including the framework's clock-skew allowance.
3. A string `client_id` present in Inventory's allowlist.
4. A `typ` header of `JWT` or `at+jwt`.
5. If the provider includes `token_use`, its value must be `access`.
6. The scope required by the requested operation.

A provider issuing tokens with a different audience or client-identity claim needs an explicit contract adjustment and tests. Do not disable audience validation or client restrictions to make a mismatched token pass. Provider compatibility has not been established merely by implementing generic OAuth/JWT support.

Namespaced scope example: if the provider grants `inventory/reserve`, set Orders' `Inventory__Scope` and Inventory's `INVENTORY_JWT_RESERVATION_SCOPE` to that exact value. Stock administration should have a separate client/grant. The provider must constrain each client's available scopes; the API checks the scope actually present in the signed token.

This is service-to-service access-token validation. It does not replace the existing public Customer registration/login flow with OpenID Connect.

## 5. Understand lookup, caching, and rotation

The decoder fetches keys from the configured URL when needed; it does not derive a URL from an incoming token's issuer or headers. The HTTP transport disables redirects and uses three-second connection/read timeouts. HTTPS certificate verification remains enabled.

Spring/Nimbus handles signing-key caching. Tests verify that a token carrying a new signing-key ID causes the decoder to obtain the updated JWKS and validate the new signature. Operators must publish new public keys before issuing tokens with them and retain old keys for the overlap period required by outstanding tokens and caches.

A provider-key outage cannot authenticate a token whose signature cannot be verified. Already cached signing keys may continue validating matching, unexpired tokens during an outage. Removing a key is not instantaneous revocation across caches; review provider-specific cache and emergency-revocation procedures before production use.

Use provider RSA signing keys of at least 2048 bits and review their rotation policy. Only RS256 is accepted by this implementation.

## 6. Verify authentication and permissions

Run from `services/inventory-service` with JDK 25:

```powershell
./mvnw.cmd --batch-mode --no-transfer-progress verify
```

On Linux use `sh ./mvnw --batch-mode --no-transfer-progress verify`.

The provider decoder tests sign real JWTs with temporary in-memory RSA keys and mock only the HTTPS JWKS response. They verify valid clients, key rotation, wrong issuer/audience/client, missing client ID, missing/expired expiration, future `nbf`, invalid signature, unknown signing-key ID, wrong algorithm, malformed token, unsupported token type, unsafe trust settings, and JWKS outages.

Provider-mode HTTP tests start the actual Spring application and replace only the JWKS HTTP transport. They verify:

- Anonymous, disallowed-client, and explicit ID-token requests return 401.
- A valid token without the required scope returns 403.
- A reservation client cannot administer stock.
- A permitted provider token can reserve, look up its reservation, and release it.
- Namespaced reservation and stock scopes work through the actual security filter chain.

Then run the regression suite from the repository root:

```powershell
python scripts/e2e-orders.py
```

That suite uses disposable PostgreSQL databases and LocalRsa mode to verify existing Orders/Inventory compatibility and restart recovery. It does not contact a real identity provider.

## 7. Complete the next deployment prerequisites

1. Select/configure the real provider and dedicated confidential Orders client.
2. Verify its token endpoint supports the Section 12.5 authentication method and its tokens satisfy this section's claims contract.
3. Test a real client-credentials exchange against Inventory before enabling Orders integration in AWS.
4. Prepare private Java hosting, database configuration, HTTPS connectivity, health checks, logs, and secret injection through Terraform and approved deployment automation.
5. Review and apply Orders database migrations separately.
6. Enable the integration and execute the order, replay, permissions, outage, and recovery smoke checks.

Keep the existing AWS API running while these prerequisites are completed.

## 8. Verification record — November 27, 2026

- All 30 Java tests passed, including eight new provider decoder/HTTP test methods. These tests cover multiple invalid-token cases within individual methods.
- The Linux container build also passed all 30 Java tests.
- All 20 disposable PostgreSQL/two-service integration assertions passed, including concurrent replay and restart recovery under LocalRsa mode.
- Test containers, database volumes, and generated private keys were removed. Existing local development services remained running.
- No live authorization server or AWS deployment was tested. GitHub CI must run after this branch is pushed.
