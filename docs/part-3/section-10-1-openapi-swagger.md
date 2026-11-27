# Section 10.1 — Local OpenAPI Documents and Swagger UI

This milestone documents the existing .NET Order API and Java Inventory lookup. Reservation/release endpoints will be added in the next milestone; documentation does not implement them.

## 1. What each component does

- OpenAPI is the machine-readable contract generated from the running application.
- Swagger UI displays that contract and sends requests when you use Try it out.
- Swagger UI does not grant permissions. Existing API authentication, roles, ownership checks, and validation still apply.

The .NET service retains Microsoft's built-in document generator and uses Swashbuckle only for its UI. Java uses springdoc's WebMVC UI starter, compatible with Spring Boot 4.1. Package versions are pinned in each project file.

## 2. Enable locally

Do not change the API to Development on AWS simply to enable documentation.

For the existing Docker Compose setup:

1. In the excluded `.env.local`, add `ApiDocs__Enabled=true`.
2. In the excluded `.env.inventory.local`, add `API_DOCS_ENABLED=true`.
3. Retain the existing database and JWT settings. Both applications still need their normal startup configuration and reachable databases.
4. From the repository root, rebuild:

```powershell
docker compose up -d --build api inventory
docker compose logs --tail 50 api inventory
```

5. Open:

| Service | Swagger UI | OpenAPI JSON |
|---|---|---|
| Order API | http://localhost:5080/swagger/index.html | http://localhost:5080/openapi/v1.json |
| Inventory | http://localhost:5081/swagger-ui/index.html | http://localhost:5081/v3/api-docs |

For direct process startup, use the same environment flags and your configured ports. .NET defaults documentation on in Development and off in other environments; explicit `ApiDocs__Enabled=false` disables it even in Development. Java defaults both document and UI off until `API_DOCS_ENABLED=true`.

## 3. Explore the Order API

1. Open the Order API Swagger page. Check the title and available Products, Orders, Users, and Authentication operations.
2. Expand a public GET products operation, select Try it out, then Execute.
3. Review the generated request, response code, and body. You are calling the actual local API and can change its data.
4. Register a Customer with your own unique email and password, or use an existing local account.
5. Call POST auth/login with those credentials.
6. Copy only the returned accessToken, without quotation marks.
7. Click Authorize at the top, paste the token without the Bearer prefix, authorize, and close the dialog.
8. Call GET auth/me. Confirm your expected account and roles.
9. Use a Customer token to check own-order access. Product changes require Admin, and Admin order creation requires an explicit userId.
10. For Admin operations, use an account legitimately assigned Admin in that environment. AWS and local accounts/databases are separate.
11. Without a token protected operations should return 401; Customer attempts at Admin operations should return 403. Some ownership checks deliberately return 404 to avoid revealing another customer's resource.
12. Tokens expire. Log in again and replace the token if needed. Authorization is not persisted across page reloads by this configuration.

The document marks protected controller operations with a Bearer requirement and describes role restrictions. Public login, registration, and product reads are not marked as requiring a token. Documentation metadata describes behavior; endpoint enforcement remains authoritative.

## 4. Explore Inventory

1. Open Inventory Swagger UI.
2. Expand GET /api/inventory/{productId} and select Try it out.
3. Enter a seeded product ID such as 1 and Execute. Check availableQuantity against the local database.
4. Try a missing ID and a nonpositive ID. The current API returns 404 and 400 respectively.
5. This endpoint only reads stock. It does not reserve inventory, and the Java service does not yet enforce JWT authentication. Do not interpret documentation as completion of those future milestones.

## 5. AWS exposure is a separate deployment choice

These source changes do not update the running ECS image. The current AWS API keeps documentation off unless explicitly enabled. No AWS changes or infrastructure teardown were performed for this milestone.

To expose documentation later, review the intended access policy, enable the documentation flag in the task configuration, publish and deploy the updated API image, and verify both document and UI routes over HTTPS. Documentation flags control visibility, not access permissions. Swagger UI itself is not an authentication boundary. Keep the public API in Production and retain the trusted proxy configuration.

## 6. Verification

- .NET tests check that enabled docs have real routes, a Bearer scheme, correct public/protected distinctions, and a working UI configuration; documentation-disabled routes return 404. Existing endpoint authorization is also exercised.
- Java tests request the actual generated document and UI over HTTP, check Inventory responses, and verify normal lookup still works. The existing suite checks normal and invalid Inventory behavior.
- Run `.NET` tests from the root with `dotnet test OrderManagementSystem.sln --configuration Release`.
- Run Java checks from `services/inventory-service` with `.\mvnw.cmd --batch-mode --no-transfer-progress clean verify` using JDK 25.

## Sources

- [Microsoft: OpenAPI customization](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0)
- [Microsoft: using OpenAPI documents and interactive UIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/using-openapi-documents?view=aspnetcore-10.0)
- [springdoc: setup and compatibility](https://springdoc.org/)

[Continue to Section 10.2 — AWS deployment](section-10-2-openapi-swagger-aws.md)

[Back to README](../../README.md)
