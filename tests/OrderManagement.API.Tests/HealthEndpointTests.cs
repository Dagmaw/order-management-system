using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using OrderManagement.API.Tests.Infrastructure;

namespace OrderManagement.API.Tests;

public class HealthEndpointTests
{
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("https://localhost")]
    public async Task Health_IsAnonymousAndDoesNotRedirect(string baseAddress)
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Null(response.Headers.Location);
    }
}
