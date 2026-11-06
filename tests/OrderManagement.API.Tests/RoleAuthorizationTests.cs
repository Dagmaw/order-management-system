using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.API.Tests.Infrastructure;
using OrderManagement.Application.DTOs;
using OrderManagement.Infrastructure.Identity;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace OrderManagement.API.Tests;

public class RoleAuthorizationTests
{
    private const string Email = "role-test@example.com";
    private const string Password = "A unique role test passphrase 2026!";

    private static HttpClient CreateClient(TestApiFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    private static async Task RegisterAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                name = "Role Test Customer",
                email = Email,
                password = Password
            });

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = Email,
                password = Password
            });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<LoginResponseDto>();

        Assert.NotNull(result);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                result.AccessToken);
    }

    [Fact]
    public async Task Customer_CannotCreateProduct()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        await RegisterAccountAsync(client);
        await LoginAsync(client);

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = "Restricted Product",
                description = "Customer must not create this",
                price = 10.00m
            });

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotListAllUsers()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        await RegisterAccountAsync(client);
        await LoginAsync(client);

        // Act
        using var response = await client.GetAsync(
            "/api/users");

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateProduct()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        await RegisterAccountAsync(client);

        // Assign Admin directly through Identity for test setup.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var account = await userManager.FindByEmailAsync(Email);

            Assert.NotNull(account);

            var roleResult = await userManager.AddToRoleAsync(
                account,
                "Admin");

            Assert.True(roleResult.Succeeded);
        }

        // Login after assigning the role so the JWT contains Admin.
        await LoginAsync(client);

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = "Admin Product",
                description = "Created by an administrator",
                price = 10.00m
            });

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var product = await response.Content
            .ReadFromJsonAsync<ProductDto>();

        Assert.NotNull(product);
        Assert.Equal("Admin Product", product.Name);
        Assert.Equal(10.00m, product.Price);
    }
}