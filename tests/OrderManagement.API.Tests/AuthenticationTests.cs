using Microsoft.AspNetCore.Mvc.Testing;
using OrderManagement.API.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;

namespace OrderManagement.API.Tests;

public class AuthenticationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-valid-token")]
    public async Task CurrentUser_WithoutValidToken_Returns401(
        string? token)
    {
        // Arrange
        using var factory = new TestApiFactory();

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);
        }

        // Act
        using var response = await client.GetAsync(
            "/api/auth/me");

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }
}