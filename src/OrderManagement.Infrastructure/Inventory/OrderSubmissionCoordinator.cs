using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Inventory;

public record SubmissionResult(Guid Id, string State, int? OrderId, string? FailureReason);
public record FrozenItem(int ProductId, int Quantity, decimal UnitPrice);
public record FrozenOrder(int UserId, decimal TotalAmount, FrozenItem[] Items);

public sealed class OrderSubmissionCoordinator(ApplicationDbContext db, IOrderSubmissionStore store,
    IOrderService orders, IInventoryClient inventory, TimeProvider clock, ILogger<OrderSubmissionCoordinator> logger)
{
    public async Task<SubmissionResult> SubmitAsync(string actor, Guid key, CreateOrderDto request, CancellationToken ct)
    {
        if (key == Guid.Empty || string.IsNullOrWhiteSpace(actor)) throw new OrderValidationException("A valid Idempotency-Key UUID is required.");
        if (request.Items is null || request.Items.Count is < 1 or > 10
            || request.Items.Any(item => item is null || item.ProductId <= 0 || item.Quantity is < 1 or > 1000)
            || request.Items.Select(item => item.ProductId).Distinct().Count() != request.Items.Count)
            throw new OrderValidationException("Integrated orders require 1–10 unique products with quantities from 1 to 1000.");
        var canonical = JsonSerializer.Serialize(new { request.UserId, Items = request.Items.OrderBy(item => item.ProductId)
            .Select(item => new { item.ProductId, item.Quantity }) });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var existing = await db.OrderSubmissions.AsNoTracking().SingleOrDefaultAsync(row => row.ActorAccountId == actor && row.RequestKey == key, ct);
        if (existing is not null && (existing.UserId != request.UserId || existing.RequestHash != hash))
            throw new DataConflictException("This request key already belongs to a different order payload.");
        if (existing is null)
        {
            var draft = await orders.PrepareOrderAsync(request, ct);
            var frozen = new FrozenOrder(draft.UserId, draft.TotalAmount,
                draft.Items.OrderBy(item => item.ProductId).Select(item => new FrozenItem(item.ProductId, item.Quantity, item.UnitPrice)).ToArray());
            existing = await store.GetOrCreateAsync(new(actor, key, draft.UserId, hash, JsonSerializer.Serialize(frozen)), ct);
        }
        await ProcessAsync(existing.Id, ct);
        return (await GetOwnedAsync(existing.Id, actor, ct))!;
    }
    public async Task<SubmissionResult?> GetOwnedAsync(Guid id, string actor, CancellationToken ct)
    {
        var row = await store.GetOwnedAsync(id, actor, ct);
        if (row is null) return null;
        var orderId = await db.Orders.AsNoTracking().Where(order => order.InventorySubmissionId == id)
            .Select(order => (int?)order.Id).SingleOrDefaultAsync(ct);
        return new(id, row.State, orderId, row.FailureReason);
    }
    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        var row = await store.TryAcquireAsync(id, owner, TimeSpan.FromSeconds(30), ct);
        if (row is null) return;
        try
        {
            var snapshot = JsonSerializer.Deserialize<FrozenOrder>(row.SnapshotJson)
                ?? throw new InvalidOperationException("Missing order snapshot.");
            if (row.State == "Pending")
            {
                try
                {
                    await inventory.ReserveAsync(id, snapshot.Items.Select(item => new InventoryReservationItem(item.ProductId,item.Quantity)).ToArray(), ct);
                    if (!await store.TransitionAsync(id, owner, "Pending", "Reserved", ct)) return;
                    row.State = "Reserved";
                }
                catch (InventoryRejectedException error) when (error.StatusCode is 400 or 404 or 409)
                {
                    // A durable release marker fences late/uncertain reserve calls before terminal failure.
                    if (!await BeginCompensationAsync(id, owner, "Pending", "Inventory could not reserve the requested stock.", ct)) return;
                    row.State = "Compensating";
                }
            }
            if (row.State == "Reserved")
            {
                try { await CompleteOrderAsync(row, owner, snapshot, ct); return; }
                catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
                {
                    db.ChangeTracker.Clear();
                    if (!await BeginCompensationAsync(id, owner, "Reserved", "A selected user or product is no longer available.", ct)) return;
                    row.State = "Compensating";
                }
            }
            if (row.State == "Compensating")
            {
                await inventory.ReleaseAsync(id, ct);
                await store.TransitionAsync(id, owner, "Compensating", "Compensated", ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Leave durable state for retry. Never release on an ambiguous database commit.
            db.ChangeTracker.Clear();
            logger.LogWarning("Submission {SubmissionId} remains pending recovery. ErrorType: {ErrorType}", id, error.GetType().Name);
        }
        finally
        {
            try { await store.ReleaseLeaseAsync(id, owner, CancellationToken.None); }
            catch { logger.LogWarning("Submission {SubmissionId} lease will expire for recovery.",id); }
        }
    }
    private async Task CompleteOrderAsync(OrderSubmission row, Guid owner, FrozenOrder snapshot, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        // This conditional UPDATE serializes with compensation and prevents stale workers from committing.
        var changed = await db.OrderSubmissions.Where(item => item.Id == row.Id && item.State == "Reserved"
                && item.LeaseOwner == owner && item.LeaseUntilUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State,"Completed")
                .SetProperty(item => item.UpdatedAtUtc,now).SetProperty(item => item.Version,item=>item.Version+1),ct);
        if (changed != 1) return;
        var order = new Order { UserId=snapshot.UserId, TotalAmount=snapshot.TotalAmount,
            InventorySubmissionId=row.Id, CreatedAt=row.CreatedAtUtc,
            Items=snapshot.Items.Select(item=>new OrderItem { ProductId=item.ProductId,Quantity=item.Quantity,UnitPrice=item.UnitPrice }).ToList() };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }
    private async Task<bool> BeginCompensationAsync(Guid id, Guid owner, string state, string reason, CancellationToken ct)
    {
        var now=clock.GetUtcNow().UtcDateTime;
        return await db.OrderSubmissions.Where(row=>row.Id==id && row.State==state && row.LeaseOwner==owner
                && row.LeaseUntilUtc>now && !db.Orders.Any(order=>order.InventorySubmissionId==id))
            .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.State,"Compensating")
                .SetProperty(row=>row.FailureReason,reason).SetProperty(row=>row.UpdatedAtUtc,now)
                .SetProperty(row=>row.Version,row=>row.Version+1),ct)==1;
    }
}
