using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Exceptions;
using OrderManagement.Infrastructure.Inventory;
namespace OrderManagement.API.Tests;
public class OAuthInventoryTokenTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return send(request, ct); }
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private static InventoryClientOptions Settings() => new()
    {
        AuthenticationMode = "ClientCredentials", BaseUrl = "https://inventory.example/",
        TokenEndpoint = "https://identity.example/oauth/token", ClientId = "orders:client",
        ClientSecret = "private+test value", Scope = "inventory.reserve"
    };
    private static HttpResponseMessage Token(string token = "service-token", int lifetime = 120)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = token, token_type = "Bearer", expires_in = lifetime, scope = "inventory.reserve" }) };
    [Fact]
    public async Task ConcurrentRequests_UseOneTokenRequestAndCorrectCredentials()
    {
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal("https://identity.example/oauth/token", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal("orders%3Aclient:private%2Btest+value", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("grant_type=client_credentials", body);
            Assert.Contains("scope=inventory.reserve", body);
            Assert.DoesNotContain("private", body);
            await Task.Delay(20, ct); return Token();
        });
        using var provider = new OAuthInventoryTokenProvider(new Factory(handler), Options.Create(Settings()), new Clock());
        var replies = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => provider.GetTokenAsync().AsTask()));
        Assert.All(replies, token => Assert.Equal("service-token", token));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task TokenRefreshesBeforeExpiry()
    {
        var clock = new Clock();
        using var handler = new Handler((_, _) => Task.FromResult(Token("token-" + clock.Now.ToUnixTimeSeconds(), 90)));
        using var provider = new OAuthInventoryTokenProvider(new Factory(handler), Options.Create(Settings()), clock);
        var first = await provider.GetTokenAsync();
        clock.Now = clock.Now.AddSeconds(59);
        Assert.Equal(first, await provider.GetTokenAsync());
        clock.Now = clock.Now.AddSeconds(2);
        Assert.NotEqual(first, await provider.GetTokenAsync()); Assert.Equal(2, handler.Calls);
    }
    [Theory]
    [InlineData(302)] [InlineData(400)] [InlineData(401)] [InlineData(429)] [InlineData(503)]
    public async Task FailedTokenRequest_IsNotRetriedOrCached(int status)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent("private+test value") }));
        using var provider = new OAuthInventoryTokenProvider(new Factory(handler), Options.Create(Settings()), new Clock());
        var error = await Assert.ThrowsAsync<InventoryUnavailableException>(() => provider.GetTokenAsync().AsTask());
        Assert.False(error.OutcomeUnknown); Assert.DoesNotContain("private", error.ToString()); Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"unsafe token\",\"token_type\":\"Bearer\",\"expires_in\":120}")]
    [InlineData("{\"access_token\":\"token\",\"token_type\":\"Basic\",\"expires_in\":120}")]
    [InlineData("{\"access_token\":\"token\",\"token_type\":\"Bearer\",\"expires_in\":0}")]
    [InlineData("{\"access_token\":\"token\",\"token_type\":\"Bearer\",\"expires_in\":120,\"scope\":\"inventory.stock.write\"}")]
    public async Task InvalidResponses_AreRejected(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") }));
        using var provider = new OAuthInventoryTokenProvider(new Factory(handler), Options.Create(Settings()), new Clock());
        Assert.False((await Assert.ThrowsAsync<InventoryUnavailableException>(() => provider.GetTokenAsync().AsTask())).OutcomeUnknown);
    }
    [Fact]
    public async Task Cancellation_DoesNotSendTokenRequest()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Token()));
        using var provider = new OAuthInventoryTokenProvider(new Factory(handler), Options.Create(Settings()), new Clock());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetTokenAsync(cancelled.Token).AsTask());
        Assert.Equal(0, handler.Calls);
    }
    [Theory]
    [InlineData("http://identity.example/token")]
    [InlineData("https://user:password@identity.example/token")]
    [InlineData("https://identity.example/token?secret=bad")]
    [InlineData("https://identity.example/token#fragment")]
    public void InvalidTokenEndpoints_FailValidation(string endpoint)
    {
        var options = Settings(); options.TokenEndpoint = endpoint;
        Assert.False(options.IsValid()); Assert.True(Settings().IsValid());
    }
    [Fact]
    public async Task FailedRefreshDoesNotReuseOldToken_AndLaterAcquisitionRecovers()
    {
        var clock = new Clock();
        var unavailable = false;
        using var handler = new Handler((_, _) => Task.FromResult(unavailable
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Token()));
        using var provider = new OAuthInventoryTokenProvider(new Factory(handler), Options.Create(Settings()), clock);
        Assert.Equal("service-token", await provider.GetTokenAsync());
        clock.Now = clock.Now.AddSeconds(121);
        unavailable = true;
        await Assert.ThrowsAsync<InventoryUnavailableException>(() => provider.GetTokenAsync().AsTask());
        unavailable = false;
        Assert.Equal("service-token", await provider.GetTokenAsync());
        Assert.Equal(3, handler.Calls);
    }
}
