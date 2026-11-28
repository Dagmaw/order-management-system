# Section 12.5 — Orders Service Identity Adapter

## 1. Goal and current boundary

Section 12.4 verified local Orders-to-Inventory transactions and recovery. This section adds an OAuth 2.0 client-credentials token adapter to the .NET Orders API. The API can now obtain a service access token from a configured HTTPS authorization server instead of signing its own development token.

This is the Orders-side foundation. No identity-provider account, Java JWKS validation configuration, AWS secrets, network route, or ECS deployment is created here. AWS Inventory integration remains disabled. User registration and login continue using the existing account/JWT implementation.

OAuth client credentials identifies an application acting on its own behalf. It does not introduce a human OpenID Connect login screen. The protocol used here is documented in [RFC 6749, section 4.4](https://www.rfc-editor.org/rfc/rfc6749.html#section-4.4).

## 2. Review the two authentication modes

`Inventory:AuthenticationMode` selects exactly one mode:

| Mode | Token source | Permitted environment |
| --- | --- | --- |
| `LocalRsa` (default) | Existing development private key signs a short-lived RS256 service token | Development only |
| `ClientCredentials` | External HTTPS token endpoint authenticates the Orders application and issues an access token | Development or production, with validated settings |

`Inventory:Enabled` still defaults to false. Enabling the local signer outside Development fails startup. An unknown authentication mode also fails. Outside Development, Inventory's base URL must use HTTPS.

## 3. Inspect asynchronous token acquisition

`IInventoryServiceTokenProvider.GetTokenAsync` now accepts cancellation. The local adapter implements the same asynchronous contract while retaining its existing signing logic. `InventoryHttpClient` awaits token acquisition before sending a reservation request.

The overall Inventory request deadline includes token acquisition. If it expires before any Inventory request is sent, the failure is classified as an unavailable dependency with a known unattempted Inventory mutation. A timeout after a reservation request may have reached Inventory retains the existing unknown-outcome classification and durable recovery behavior.

## 4. Understand the OAuth adapter

`OAuthInventoryTokenProvider` sends one form-encoded POST with `grant_type=client_credentials` and the configured scope. It authenticates with `client_secret_basic`: form-encoded client ID and secret are joined with a colon and Base64 encoded for the Authorization header, as specified by [RFC 6749, section 2.3.1](https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1).

The named HTTP client rejects redirects, limits the response to 64 KiB, and has a 10-second timeout. A shorter Inventory operation deadline can cancel it earlier. Token errors are not automatically retried within the same operation. Response bodies and credentials are not included in application exceptions.

The singleton adapter caches a token in memory and uses a semaphore so concurrent callers share a successful acquisition. It refreshes 30 seconds before the declared expiration. Supported token lifetimes are 31–86,400 seconds; a response arriving after its refresh boundary is rejected. Responses must contain a Bearer token, a supported expiration, and, if a scope is returned, all requested permissions. Malformed JSON, missing tokens, invalid token types, and unsuccessful HTTP responses fail closed. No refresh token is needed for this grant.

The adapter consumes the authorization server's token response; Inventory must independently validate JWT signature, issuer, audience, expiration, and permissions. A mocked token endpoint test is not evidence that a real provider is configured correctly.

## 5. Configure a future provider without committing credentials

These are the environment-variable names for ECS or a local secret source:

| Name | Value or purpose |
| --- | --- |
| `Inventory__Enabled` | `true` only after the complete integration is ready |
| `Inventory__AuthenticationMode` | `ClientCredentials` |
| `Inventory__BaseUrl` | HTTPS base URL of the private Inventory service, ending in `/` |
| `Inventory__TimeoutSeconds` | Operation deadline, 1–30 seconds |
| `Inventory__TokenEndpoint` | Provider's exact HTTPS token endpoint; no credentials, query, or fragment in the URL |
| `Inventory__ClientId` | Orders application's dedicated client ID |
| `Inventory__ClientSecret` | Secret injected by a secret store; never a committed Terraform literal |
| `Inventory__Scope` | Provider scope granting reservation/release access, normally `inventory.reserve` |

Do not set a private-key path for this mode. The Orders client must receive reservation permission only, not stock-administration permission. Some providers use namespaced scope names; map the final provider contract consistently on both services rather than assuming a provider's defaults match the local contract.

Before enabling this in AWS:

1. Select and configure the authorization server and a confidential Orders application supporting `client_secret_basic`.
2. Define the Inventory API audience and reservation scope. Confirm the provider issues JWT access tokens with the required claims and RS256 signing.
3. Add Inventory's provider/JWKS validation mode and test valid tokens, wrong issuer/audience, expired tokens, missing scopes, and signing-key rotation. Spring's supported approach is described in its [JWT resource-server documentation](https://docs.spring.io/spring-security/reference/servlet/oauth2/resource-server/jwt.html).
4. Store the client secret in AWS Secrets Manager, grant only the required execution-role access, and inject it into the API task definition.
5. Deploy Java privately with its own database settings and trusted HTTPS endpoint. Verify health and network connectivity.
6. Apply the required Orders database migrations through the approved migration process.
7. Enable the integration in a reviewed deployment and repeat the order, authorization, replay, outage, and recovery smoke checks.

Do not enable the external-client mode against the current development Java public key and expect authentication to work. The issuer and trust configuration must match.

## 6. Run verification

From the repository root:

```powershell
dotnet test OrderManagementSystem.sln --configuration Release --verbosity minimal
dotnet build tools/InventoryClientProbe/InventoryClientProbe.csproj --configuration Release
python scripts/e2e-orders.py
```

The token-adapter tests use a controlled HTTP handler and fake clock. They verify credential encoding, one acquisition for concurrent callers, refresh before expiry, HTTP failure handling, malformed-response rejection, cancellation, and endpoint validation. Boundary tests reject production local signing and plain HTTP Inventory connections, and verify that token timeouts do not send a stock mutation.

The disposable two-service suite continues to use LocalRsa. It verifies compatibility with the existing development flow, not an external identity provider.

## 7. Operational limits

Cached tokens remain usable until the refresh boundary. Client-secret rotation requires an ECS task restart in this implementation because configuration is captured through `IOptions`. Provider outages leave submissions available for durable recovery; production retry backoff, alerting, provider-specific rate limits, and revocation behavior still need operational review. There is no automatic switch back to local signing.

## 8. Verification record — November 27, 2026

- All 98 .NET tests passed (13 Application and 85 API/infrastructure tests), including 22 new identity-adapter and boundary cases.
- All 20 disposable PostgreSQL/two-service end-to-end assertions passed using the preserved LocalRsa mode.
- The InventoryClientProbe project builds with zero warnings and errors.
- Test containers, database volumes, and generated private keys were cleaned up. Existing local development services stayed running.
- No real authorization server, Java JWKS configuration, or AWS deployment has been tested in this milestone. GitHub CI must still run after the changes are pushed.
