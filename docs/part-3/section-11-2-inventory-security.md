# Section 11.2 — Local Inventory Security

## Design

Inventory is a stateless Spring Security OAuth2 resource server validating dedicated RS256 access tokens. It checks the RSA signature, issuer, audience, expiration/not-before, required expiration, and nonempty subject. Only the public verification key is loaded by Inventory. A 2048-bit or stronger key is required; missing/invalid configuration fails startup.

This milestone is not an OpenID Connect provider and does not implement user login or client credentials. The local token helper simulates a trusted issuer for development only. A future identity-provider milestone will replace local issuance with standard token acquisition and key discovery. Do not deploy the local private key or helper as a public authentication service.

The .NET API's HS256 user tokens are intentionally not accepted: Inventory has its own audience, issuer, and asymmetric signing key. No AWS secrets were copied locally.

## Permission matrix

| Operation | Required permission |
|---|---|
| GET inventory/{productId} | Public |
| Swagger/OpenAPI | Public when enabled by configuration |
| PUT inventory/{productId}/stock | `inventory.stock.write` scope |
| Reserve, inspect reservation, release | `inventory.reserve` scope |
| Other paths | Denied by default |

Missing/invalid/expired tokens return 401. Valid tokens missing the required scope return 403. Scope checks run before controllers and database changes. The reservation scope is for the trusted Order service, not individual Customers. There is one trusted Order integration in this milestone; separate tenant/client reservation ownership would require an additional data model and policy.

CSRF protection is disabled for this stateless Bearer-token API; it uses no authentication cookies or browser sessions. This policy must be reconsidered if cookie authentication is introduced.

## Local token preparation

The helper `scripts/inventory-token.py` requires Python and the cryptography package. Use an existing trusted Python environment, or install that package in a local virtual environment. From the repository root:

```powershell
python scripts/inventory-token.py customer
python scripts/inventory-token.py stock-admin
python scripts/inventory-token.py order-service
```

The helper creates an RSA private key under excluded `.local-inventory-auth/`, puts only the public verification key into excluded `.env.inventory.local`, and writes each token to its own excluded text file. It does not print tokens or private keys. `.gitignore` and the root `.dockerignore` exclude that directory. Tokens last 15 minutes; rerun for a fresh one.

Keep the existing Inventory database settings, `API_DOCS_ENABLED=true`, and `INVENTORY_OPERATIONS_ENABLED=true` locally. Rebuild/recreate the container after adding the public key:

```powershell
docker compose up -d --build --no-deps inventory
```

Use the helper with your own local files only. Its private key is unencrypted development material and must remain on your development computer. It is not an enterprise production identity provider.

## Swagger verification

1. Refresh `http://localhost:5081/swagger-ui/index.html` and confirm the Authorize button and new security descriptions.
2. With no token, execute GET inventory/1: expect 200.
3. With no token, attempt a reservation: expect 401 and unchanged stock.
4. Generate/open the customer token, copy its value into Authorize without the Bearer prefix, and attempt a reservation: expect 403.
5. Logout in the Authorize dialog and replace with the order-service token.
6. Submit a valid reservation with a fresh UUID and a quantity within available stock: expect 200 RESERVED.
7. Attempt PUT stock with this order-service token: expect 403. The service cannot perform administrative stock adjustments.
8. Release the reservation using that same order-service token: expect 200 RELEASED. Verify the stock was restored.
9. Replace authorization with the stock-admin token. Read the latest stock version, then submit a deliberate PUT stock update with that version: expect 200.
10. Attempt a reservation with only the stock-admin scope: expect 403. Administrative stock correction does not implicitly grant Order-service permission.
11. Clear authorization when finished; hide tokens in request/Curl sections of screenshots and never export them to Git.

Generating a new token with an existing key does not require restarting Inventory. Changing the verification key requires reconfiguration/recreation.

## Verification record — 27 November 2026

Java clean verify passed all 22 tests, including the new real-signature security cases and the existing transaction/concurrency suite. The rebuilt local Docker service was also verified: public lookup 200; anonymous reservation 401; Customer reservation 403; Order-service stock update 403; authorized invalid payloads 400. These live checks did not change stock. Swagger includes the Bearer scheme and reservation security requirement. AWS was not changed.

Manual verification of all Section 11.2 permission checks was also confirmed by the user.

## Automated checks and next steps

Tests use freshly generated RSA keys and actual signed JWTs, not an authentication bypass. They cover anonymous 401, insufficient-scope 403, issuer/audience/signature/expiry failures, missing expiration, valid authorized requests, and public lookup. Section 11.1 transaction/concurrency tests remain in the suite and their HTTP requests now authenticate.

The next milestone is .NET-to-Java integration: token acquisition, timeouts, reservation identity, retries, and compensation. Java is still local; AWS remains running and unchanged.

[Spring Security JWT resource server reference](https://docs.spring.io/spring-security/reference/servlet/oauth2/resource-server/jwt.html)

[Back to README](../../README.md)
