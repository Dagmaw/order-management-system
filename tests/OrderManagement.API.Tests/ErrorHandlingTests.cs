using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderManagement.API.Tests.Infrastructure;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.API.Tests;

public class ErrorHandlingTests
{
    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    [Theory]
    [InlineData("duplicate", 409, "Email already registered")]
    [InlineData(
        "registration",
        400,
        "Registration validation failed.")]
    [InlineData("order", 400, "Order validation failed")]
    [InlineData(
        "conflict",
        409,
        "Operation conflicts with existing data")]
    [InlineData("unexpected", 500, "An unexpected error occurred")]
    public async Task Exception_ReturnsProblemDetailsWithCorrelationId(
        string scenario,
        int expectedStatus,
        string expectedTitle)
    {
        // Arrange: replace the service only in this test host.
        using var factory = new TestApiFactory();

        using var configuredFactory = factory.WithWebHostBuilder(
            builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAuthService>();

                    services.AddScoped<IAuthService>(
                        _ => new ThrowingAuthService(scenario));
                });
            });

        using var client = CreateClient(configuredFactory);

        // Act: send a valid request that reaches the test service.
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                name = "Error Handling Test",
                email = "error-test@example.com",
                password = "A unique error test passphrase 2026!"
            });

        // Assert: status and Problem Details content type.
        Assert.Equal(
            (HttpStatusCode)expectedStatus,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            expectedStatus,
            body.GetProperty("status").GetInt32());

        Assert.Equal(
            expectedTitle,
            body.GetProperty("title").GetString());

        Assert.Equal(
            "/api/auth/register",
            body.GetProperty("instance").GetString());

        // Header and error-body identifiers must match.
        Assert.True(
            response.Headers.TryGetValues(
                "X-Correlation-ID",
                out var headerValues));

        var correlationId = Assert.Single(headerValues!);

        Assert.False(
            string.IsNullOrWhiteSpace(correlationId));

        Assert.Equal(
            correlationId,
            body.GetProperty("correlationId").GetString());

        Assert.Equal(
            correlationId,
            body.GetProperty("traceId").GetString());

        if (scenario == "registration")
        {
            var registrationErrors = body
                .GetProperty("errors")
                .GetProperty("Registration");

            Assert.Equal(
                "A test registration rule failed.",
                registrationErrors[0].GetString());
        }

        if (scenario == "unexpected")
        {
            Assert.Equal(
                "The request could not be completed.",
                body.GetProperty("detail").GetString());

            var responseText = await response.Content
                .ReadAsStringAsync();

            Assert.DoesNotContain(
                "INTERNAL_TEST_SECRET",
                responseText);

            Assert.DoesNotContain(
                "InvalidOperationException",
                responseText);

            Assert.False(
                body.TryGetProperty("stackTrace", out _));
        }
    }

    [Fact]
    public async Task SuccessfulRequest_ReturnsCorrelationHeader()
    {
        // Arrange
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        // Act
        using var response = await client.GetAsync(
            "/api/products");

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.True(
            response.Headers.TryGetValues(
                "X-Correlation-ID",
                out var headerValues));

        var correlationId = Assert.Single(headerValues!);

        Assert.False(
            string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task UnauthorizedRequest_ReturnsCorrelationHeader()
    {
        // Arrange: client has no Bearer token.
        using var factory = new TestApiFactory();
        using var client = CreateClient(factory);

        // Act
        using var response = await client.GetAsync(
            "/api/auth/me");

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.True(
            response.Headers.TryGetValues(
                "X-Correlation-ID",
                out var headerValues));

        var correlationId = Assert.Single(headerValues!);

        Assert.False(
            string.IsNullOrWhiteSpace(correlationId));
    }

    private sealed class ThrowingAuthService : IAuthService
    {
        private readonly string _scenario;

        public ThrowingAuthService(string scenario)
        {
            _scenario = scenario;
        }

        public Task<UserDto> RegisterAsync(
            RegisterDto dto,
            CancellationToken cancellationToken = default)
        {
            Exception exception = _scenario switch
            {
                "duplicate" => new DuplicateEmailException(),

                "registration" =>
                    new RegistrationValidationException(
                        new[]
                        {
                            "A test registration rule failed."
                        }),

                "order" => new OrderValidationException(
                    "The selected product does not exist."),

                "conflict" => new DataConflictException(
                    "The operation conflicts with existing data."),

                _ => new InvalidOperationException(
                    "INTERNAL_TEST_SECRET: unexpected test failure.")
            };

            return Task.FromException<UserDto>(exception);
        }

        public Task<LoginResponseDto?> LoginAsync(
            LoginDto dto,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "Login is not used by this test service.");
        }
    }
}