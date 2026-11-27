using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OrderManagement.API.Tests.Infrastructure;

namespace OrderManagement.API.Tests;

public class OpenApiTests
{
    [Fact]
    public async Task EnabledDocs_DescribeRealEndpointsAndTheirSecurity()
    {
        using var factory = new TestApiFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("ApiDocs:Enabled", "true"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        Assert.Equal("Order Management API", root.GetProperty("info").GetProperty("title").GetString());
        var paths = root.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/products").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/products").GetProperty("get").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/auth/login").GetProperty("post").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/api/orders").GetProperty("post").TryGetProperty("security", out _));
        Assert.Equal("bearer", root.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.Contains("swagger-ui", await client.GetStringAsync("/swagger/index.html"));
        Assert.Contains("../openapi/v1.json", await client.GetStringAsync("/swagger/index.js"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/products", null)).StatusCode);
    }

    [Fact]
    public async Task Docs_AreNotExposedWhenDisabled()
    {
        using var factory = new TestApiFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("ApiDocs:Enabled", "false"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }
}
