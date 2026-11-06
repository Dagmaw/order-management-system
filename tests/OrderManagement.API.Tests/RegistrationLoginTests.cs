using Microsoft.AspNetCore.Mvc.Testing;
using OrderManagement.API.Tests.Infrastructure;
using OrderManagement.Application.DTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.API.Tests;

public class RegistrationLoginTests
{
    private const string Email = "customer@example.com";
    private const string Password = "A unique test passphrase 2026!";

    private static HttpClient CreateClient(TestApiFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client)
    {
        return client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                name = "Test Customer",
                email = Email,
                password = Password
            });
    }

    [Fact]
    public async Task RegisterAndLogin_ReturnsUsableCustomerToken()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        // Register
        using var registration = await RegisterAsync(client);

        Assert.Equal(
            HttpStatusCode.Created,
            registration.StatusCode);

        var user = await registration.Content
            .ReadFromJsonAsync<UserDto>();

        Assert.NotNull(user);

        // Login
        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = Email,
                password = Password
            });

        Assert.Equal(
            HttpStatusCode.OK,
            login.StatusCode);

        var result = await login.Content
            .ReadFromJsonAsync<LoginResponseDto>();

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(user.Id, result.UserId);
        Assert.Contains("Customer", result.Roles);
        Assert.DoesNotContain("Admin", result.Roles);
        Assert.True(result.ExpiresAtUtc > DateTimeOffset.UtcNow);

        // Use the issued token on a protected endpoint.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                result.AccessToken);

        using var currentUser = await client.GetAsync(
            "/api/auth/me");

        Assert.Equal(
            HttpStatusCode.OK,
            currentUser.StatusCode);

        var body = await currentUser.Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            user.Id.ToString(),
            body.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        using var registration = await RegisterAsync(client);

        Assert.Equal(
            HttpStatusCode.Created,
            registration.StatusCode);

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = Email,
                password = "An incorrect test passphrase!"
            });

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        using var firstRegistration = await RegisterAsync(client);

        Assert.Equal(
            HttpStatusCode.Created,
            firstRegistration.StatusCode);

        // Act
        using var secondRegistration = await RegisterAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            secondRegistration.StatusCode);
    }
}