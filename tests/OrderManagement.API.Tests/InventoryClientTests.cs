using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Infrastructure.Inventory;

namespace OrderManagement.API.Tests;

public class InventoryClientTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly InventoryReservationItem[] Items = [new(1, 2)];
    private sealed class Tokens : IInventoryServiceTokenProvider { public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("test-token"); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request, ct); }
    }
    private static InventoryHttpClient Client(Handler handler, int timeoutSeconds = 5)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, new Tokens(),
            Options.Create(new InventoryClientOptions { TimeoutSeconds = timeoutSeconds }));
    private static HttpResponseMessage Reservation(string status = "RESERVED") => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new InventoryReservation(Id, status, Items)) };

    [Fact]
    public async Task Reserve_SendsStableIdServiceTokenAndCorrelation()
    {
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/inventory/reservations", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            Assert.True(request.Headers.Contains("X-Correlation-ID"));
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains(Id.ToString(), body);
            Assert.Contains("\"productId\":1", body);
            return Reservation();
        });
        Assert.Equal("RESERVED", (await Client(handler).ReserveAsync(Id, Items)).Status);
        Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(409)]
    public async Task Rejections_AreNotRetriedOrReportedAsSuccess(int status)
    {
        using var handler = new Handler((request, ct) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<InventoryRejectedException>(() => Client(handler).ReserveAsync(Id, Items));
        Assert.Equal(status, error.StatusCode); Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData(302)] [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task UnexpectedResponses_LeaveOutcomeUnknownWithoutPostRetry(int status)
    {
        using var handler = new Handler((request, ct) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<InventoryUnavailableException>(() => Client(handler).ReserveAsync(Id, Items));
        Assert.True(error.OutcomeUnknown); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task Timeout_HasUnknownMutationOutcomeAndDoesNotRetry()
    {
        using var handler = new Handler(async (request, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Reservation(); });
        var error = await Assert.ThrowsAsync<InventoryUnavailableException>(() => Client(handler, 1).ReserveAsync(Id, Items));
        Assert.True(error.OutcomeUnknown); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task CallerCancellation_IsPreserved()
    {
        using var handler = new Handler((request, ct) => Task.FromResult(Reservation()));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).ReserveAsync(Id, Items, cancelled.Token));
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task Lookup404_ReturnsAbsentAndReleaseUsesStableId()
    {
        using var missing = new Handler((request, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await Client(missing).GetReservationAsync(Id));
        using var release = new Handler((request, ct) =>
        {
            Assert.Equal($"/api/inventory/reservations/{Id}/release", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(Reservation("RELEASED"));
        });
        Assert.Equal("RELEASED", (await Client(release).ReleaseAsync(Id)).Status);
    }
    [Fact]
    public async Task MalformedSuccessfulResponse_IsNotTrusted()
    {
        using var handler = new Handler((request, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json") }));
        Assert.True((await Assert.ThrowsAsync<InventoryUnavailableException>(() => Client(handler).ReserveAsync(Id, Items))).OutcomeUnknown);
    }
    [Fact]
    public void LocalServiceToken_IsSignedForInventoryWithOnlyReservationScope()
    {
        using var key = RSA.Create(2048);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
            using var provider = new InventoryServiceTokenProvider(Options.Create(new InventoryClientOptions { PrivateKeyPath = path }));
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(provider.CreateToken(), new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "Inventory.LocalIssuer",
                ValidateAudience = true, ValidAudience = "Inventory.API", ValidateLifetime = true,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new RsaSecurityKey(key),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            }, out _);
            Assert.Equal("inventory.reserve", principal.FindFirst("scope")!.Value);
            Assert.Equal("order-management-api", principal.FindFirst("sub")!.Value);
        }
        finally { File.Delete(path); }
    }
}
