using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Infrastructure.Inventory;

// One process-wide cache; credentials are sent only to the configured HTTPS token endpoint.
public sealed class OAuthInventoryTokenProvider(
    IHttpClientFactory clients, IOptions<InventoryClientOptions> options, TimeProvider clock)
    : IInventoryServiceTokenProvider, IDisposable
{
    public const string HttpClientName = "InventoryIdentityProvider";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _refreshAt;

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && clock.GetUtcNow() < _refreshAt) return _token;
            var settings = options.Value;
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.TokenEndpoint);
            var credentials = WebUtility.UrlEncode(settings.ClientId) + ":" + WebUtility.UrlEncode(settings.ClientSecret);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = settings.Scope
            });
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var requestedAt = clock.GetUtcNow();
            using var client = clients.CreateClient(HttpClientName);
            // The factory configures redirect rejection, a bounded response, and a finite timeout.
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InventoryUnavailableException(false);
            var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.AccessToken)
                || result.AccessToken.Length > 32768 || result.AccessToken.Any(char.IsWhiteSpace)
                || !string.Equals(result.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
                || result.ExpiresIn is < 31 or > 86400
                || (result.Scope is not null && !settings.Scope.Split(' ').All(s => result.Scope.Split(' ').Contains(s))))
                throw new InventoryUnavailableException(false);
            var refreshAt = requestedAt.AddSeconds(result.ExpiresIn - 30);
            if (clock.GetUtcNow() >= refreshAt) throw new InventoryUnavailableException(false);
            _token = result.AccessToken;
            _refreshAt = refreshAt;
            return _token;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InventoryUnavailableException(false); }
        catch (HttpRequestException) { throw new InventoryUnavailableException(false); }
        catch (JsonException) { throw new InventoryUnavailableException(false); }
        catch (NotSupportedException) { throw new InventoryUnavailableException(false); }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);
}
