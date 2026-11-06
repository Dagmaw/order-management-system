# Section 6 — Authentication, Roles, and Order Ownership

[← Project home](../../README.md)

> This page records the implementation at this milestone. For current startup instructions and access rules, see the project home page and Section 6. Historical image tags and sample data are preserved.

### Implemented

- Added ASP.NET Core Identity with PostgreSQL persistence.
- Kept domain users separate from Identity login accounts.
- Linked each Identity account to its domain user.
- Added Customer registration and password-based login.
- Issued signed JWT access tokens containing account ID,
  domain user ID, and role claims.
- Added JWT configuration validation at application startup.
- Protected endpoints using authentication and role-based authorization.
- Restricted Customers to their own orders.
- Added API integration tests using an isolated in-memory SQLite database.

### Authentication Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| POST | `/api/auth/register` | Register a Customer |
| POST | `/api/auth/login` | Obtain an access token |
| GET | `/api/auth/me` | Return the authenticated account's claims |

Registration assigns the Customer role.
Public registration does not grant Admin access.

### Authorization Rules

| Operation | Access |
|-----------|--------|
| Read products | Public |
| Create, update, or delete products | Admin |
| List all users | Admin |
| Retrieve a user | Own user record or Admin |
| Create a user through `/api/users` | Admin |
| List orders | Customer's own orders; Admin sees all |
| Retrieve an order | Owner or Admin |
| Create an order | Customer for self; Admin for a specified user |

### Order Ownership

Customer order creation derives the user ID from the validated JWT.

- Customers cannot create orders for another user.
- Customers cannot retrieve another Customer's order.
- Requests for another Customer's order return 404.
- Attempts to create an order for another user return 403.
- Order totals and product price snapshots are calculated on the server.

### Response Codes

| Status | Meaning |
|--------|---------|
| 200 | Successful login or retrieval |
| 201 | Account, product, or order created |
| 400 | Invalid request |
| 401 | Missing, invalid, or expired authentication |
| 403 | Authenticated account lacks permission |
| 404 | Resource unavailable to the requester |
| 409 | Duplicate email or conflicting operation |

### Local Configuration

Local configuration is supplied through `.env.local`, which is excluded
from Git.

Required JWT settings:

- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__AccessTokenMinutes`
- `Jwt__SigningKeyBase64`

The signing key must be Base64 encoded and contain at least 32 decoded
bytes. Access-token lifetime must be between 1 and 60 minutes.

Do not commit passwords, signing keys, or access tokens.

Development Admin bootstrap is disabled after the intended account
has been assigned its role.

### API Integration Tests

The API tests use:

- xUnit
- ASP.NET Core WebApplicationFactory
- In-memory SQLite
- Real registration, login, and JWT validation

Each test creates an isolated test application and database.
The database disappears when its connection closes.

The 11 API tests cover:

- Missing and invalid tokens
- Registration and successful login
- Incorrect passwords
- Duplicate email registration
- Customer restrictions
- Admin product creation
- Creating and retrieving an owned order
- Rejecting access to another Customer's order
- Rejecting order creation for another user

Run all .NET tests:

```powershell
docker compose run --rm tools sh -c 'dotnet restore OrderManagementSystem.sln --force && dotnet test OrderManagementSystem.sln --no-restore'
```

### Testing Limitations

SQLite integration tests verify application behavior but do not validate
PostgreSQL-specific migrations or provider behavior. PostgreSQL and Docker
flows are also verified manually.

### Current Scope

Authentication uses locally managed accounts and JWT access tokens.
SSO, refresh tokens, and production identity-provider integration are
not implemented in this milestone.

---

[Previous section](../part-1/section-05-ci-and-terraform.md) | [Project home](../../README.md)
