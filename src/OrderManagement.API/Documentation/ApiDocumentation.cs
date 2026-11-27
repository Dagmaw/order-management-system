using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;

namespace OrderManagement.API.Documentation;

public static class ApiDocumentation
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = "Order Management API";
                document.Info.Version = "v1";
                document.Info.Description = "Register and log in to obtain a JWT. Public registration grants Customer. "
                    + "Product changes and user administration require Admin. Customers access their own orders.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Paste the accessToken returned by login, without the Bearer prefix."
                };

                var descriptions = context.ApplicationServices
                    .GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                    .ApiDescriptionGroups.Items.SelectMany(group => group.Items);
                foreach (var api in descriptions)
                {
                    var path = "/" + api.RelativePath?.Split('?')[0];
                    if (!document.Paths.TryGetValue(path, out var item) || item.Operations is null)
                        continue;
                    var operation = item.Operations.FirstOrDefault(entry =>
                        string.Equals(entry.Key.ToString(), api.HttpMethod, StringComparison.OrdinalIgnoreCase)).Value;
                    if (operation is null) continue;
                    var metadata = api.ActionDescriptor.EndpointMetadata;
                    if (metadata.OfType<IAllowAnonymous>().Any()) continue;
                    var policies = metadata.OfType<IAuthorizeData>().ToArray();
                    if (policies.Length == 0) continue;

                    operation.Security = [new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                    }];
                    operation.Responses ??= new OpenApiResponses();
                    operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing, invalid, or expired access token." });
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Authenticated account is not permitted to perform this operation." });
                    var roles = policies.Select(policy => policy.Roles).Where(role => !string.IsNullOrWhiteSpace(role));
                    operation.Description = (operation.Description ?? "") + " Requires authentication. "
                        + (roles.Any() ? "Required role(s): " + string.Join("; ", roles) + "." : "Customer ownership rules apply where relevant.");
                }
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
