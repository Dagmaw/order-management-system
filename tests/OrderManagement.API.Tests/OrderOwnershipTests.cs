using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.API.Tests.Infrastructure;
using OrderManagement.Application.DTOs;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.API.Tests;

public class OrderOwnershipTests
{
    private const string Password =
        "A unique ownership test passphrase 2026!";

    private static HttpClient CreateClient(TestApiFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    private static async Task<LoginResponseDto> RegisterAndLoginAsync(
        HttpClient client,
        string email)
    {
        using var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                name = "Ownership Test Customer",
                email,
                password = Password
            });

        Assert.Equal(
            HttpStatusCode.Created,
            registration.StatusCode);

        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email,
                password = Password
            });

        Assert.Equal(
            HttpStatusCode.OK,
            login.StatusCode);

        var result = await login.Content
            .ReadFromJsonAsync<LoginResponseDto>();

        Assert.NotNull(result);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                result.AccessToken);

        return result;
    }

    private static async Task<int> SeedProductAsync(
        TestApiFactory factory)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var product = new Product
        {
            Name = "Ownership Test Product",
            Description = "Product for order ownership tests",
            Price = 25.00m,
            CreatedAt = DateTime.UtcNow
        };

        db.Products.Add(product);

        await db.SaveChangesAsync();

        return product.Id;
    }

    private static Task<HttpResponseMessage> CreateOrderAsync(
        HttpClient client,
        int productId)
    {
        return client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                items = new[]
                {
                    new
                    {
                        productId,
                        quantity = 2
                    }
                }
            });
    }

    [Fact]
    public async Task Customer_CanCreateAndRetrieveOwnOrder()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        var customer = await RegisterAndLoginAsync(
            client,
            "owner@example.com");

        var productId = await SeedProductAsync(factory);

        // Act: create an order without supplying a user ID.
        using var creation = await CreateOrderAsync(
            client,
            productId);

        // Assert: ownership comes from the authenticated account.
        Assert.Equal(
            HttpStatusCode.Created,
            creation.StatusCode);

        var createdOrder = await creation.Content
            .ReadFromJsonAsync<JsonElement>();

        var orderId = createdOrder
            .GetProperty("id")
            .GetInt32();

        Assert.Equal(
            customer.UserId,
            createdOrder.GetProperty("userId").GetInt32());

        Assert.Equal(
            50.00m,
            createdOrder.GetProperty("totalAmount").GetDecimal());

        // Act: retrieve the order using the owner's token.
        using var retrieval = await client.GetAsync(
            $"/api/orders/{orderId}");

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            retrieval.StatusCode);

        var retrievedOrder = await retrieval.Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            orderId,
            retrievedOrder.GetProperty("id").GetInt32());

        Assert.Equal(
            customer.UserId,
            retrievedOrder.GetProperty("userId").GetInt32());
    }

    [Fact]
    public async Task Customer_CannotRetrieveAnotherCustomersOrder()
    {
        // Arrange: two Customers share the same test application.
        using var factory = new TestApiFactory();
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);

        await RegisterAndLoginAsync(
            ownerClient,
            "owner@example.com");

        await RegisterAndLoginAsync(
            otherClient,
            "other@example.com");

        var productId = await SeedProductAsync(factory);

        using var creation = await CreateOrderAsync(
            ownerClient,
            productId);

        Assert.Equal(
            HttpStatusCode.Created,
            creation.StatusCode);

        var createdOrder = await creation.Content
            .ReadFromJsonAsync<JsonElement>();

        var orderId = createdOrder
            .GetProperty("id")
            .GetInt32();

        // Confirm the owner can retrieve the existing order.
        using var ownerResponse = await ownerClient.GetAsync(
            $"/api/orders/{orderId}");

        Assert.Equal(
            HttpStatusCode.OK,
            ownerResponse.StatusCode);

        // Act: another Customer requests the same order.
        using var otherResponse = await otherClient.GetAsync(
            $"/api/orders/{orderId}");

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            otherResponse.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotCreateOrderForAnotherUser()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var customerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);

        await RegisterAndLoginAsync(
            customerClient,
            "customer@example.com");

        var otherCustomer = await RegisterAndLoginAsync(
            otherClient,
            "other@example.com");

        var productId = await SeedProductAsync(factory);

        // Act: supply another Customer's domain user ID.
        using var response = await customerClient.PostAsJsonAsync(
            "/api/orders",
            new
            {
                userId = otherCustomer.UserId,
                items = new[]
                {
                    new
                    {
                        productId,
                        quantity = 2
                    }
                }
            });

        // Assert: reject the request.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);

        // Verify the rejected request did not save an order.
        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        Assert.False(await db.Orders.AnyAsync());
    }
}