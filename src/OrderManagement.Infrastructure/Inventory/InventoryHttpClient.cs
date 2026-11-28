using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;

namespace OrderManagement.Infrastructure.Inventory;

public sealed class InventoryHttpClient(HttpClient http, IInventoryServiceTokenProvider tokens,
    IOptions<InventoryClientOptions> options) : IInventoryClient
{
    public async Task<InventoryReservation> ReserveAsync(Guid reservationId, IReadOnlyList<InventoryReservationItem> items,
        CancellationToken cancellationToken = default)
    {
        ValidateId(reservationId);
        if (items is null || items.Count is < 1 or > 10 || items.Any(item => item is null || item.ProductId <= 0 || item.Quantity is < 1 or > 1000)
            || items.Select(item => item.ProductId).Distinct().Count() != items.Count)
            throw new ArgumentException("Inventory reservations require 1–10 unique products with quantities from 1 to 1000.", nameof(items));
        var result = await RequiredAsync(HttpMethod.Post, "api/inventory/reservations", reservationId,
            new { reservationId, items = items.OrderBy(item => item.ProductId).ToArray() }, cancellationToken);
        if (!result.Items.OrderBy(item => item.ProductId).SequenceEqual(items.OrderBy(item => item.ProductId)))
            throw new InventoryUnavailableException(true);
        return result;
    }
    public async Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        ValidateId(reservationId);
        return await SendAsync(HttpMethod.Get, $"api/inventory/reservations/{reservationId}", reservationId, null, cancellationToken);
    }
    public Task<InventoryReservation> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        ValidateId(reservationId);
        return RequiredAsync(HttpMethod.Post, $"api/inventory/reservations/{reservationId}/release", reservationId, null, cancellationToken);
    }
    private async Task<InventoryReservation> RequiredAsync(HttpMethod method, string path, Guid id, object? body, CancellationToken ct)
        => await SendAsync(method, path, id, body, ct) ?? throw new InventoryUnavailableException(true);

    private async Task<InventoryReservation?> SendAsync(HttpMethod method, string path, Guid id, object? body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using var request = new HttpRequestMessage(method, path);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Correlation-ID", Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"));
        if (body is not null) request.Content = JsonContent.Create(body);
        var sent = false;
        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(deadline.Token));
            // No automatic POST retry: a timeout can occur after the server commits.
            sent = true;
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
            if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                if (status is 400 or 401 or 403 or 404 or 409) throw new InventoryRejectedException(status);
                throw new InventoryUnavailableException(method != HttpMethod.Get);
            }
            var result = await response.Content.ReadFromJsonAsync<InventoryReservation>(deadline.Token);
            if (result is null || result.ReservationId != id || result.Status is not ("RESERVED" or "RELEASED")
                || result.Items is null || result.Items.Count > 10 || (result.Status == "RESERVED" && result.Items.Count == 0)
                || result.Items.Any(item => item is null || item.ProductId <= 0 || item.Quantity <= 0))
                throw new InventoryUnavailableException(method != HttpMethod.Get);
            if (method == HttpMethod.Post && path.EndsWith("/release") && result.Status != "RELEASED")
                throw new InventoryUnavailableException(true);
            if (method == HttpMethod.Post && !path.EndsWith("/release") && result.Status != "RESERVED")
                throw new InventoryUnavailableException(true);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new InventoryUnavailableException(sent && method != HttpMethod.Get); }
        catch (HttpRequestException) { throw new InventoryUnavailableException(method != HttpMethod.Get); }
        catch (JsonException) { throw new InventoryUnavailableException(method != HttpMethod.Get); }
        catch (NotSupportedException) { throw new InventoryUnavailableException(method != HttpMethod.Get); }
    }
    private static void ValidateId(Guid id) { if (id == Guid.Empty) throw new ArgumentException("A stable nonempty reservation ID is required."); }
}
