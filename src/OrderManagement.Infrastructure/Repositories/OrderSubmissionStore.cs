using Microsoft.EntityFrameworkCore;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class OrderSubmissionStore(ApplicationDbContext db, TimeProvider clock) : IOrderSubmissionStore
{
    private static readonly HashSet<string> Terminal = ["Completed", "Rejected", "Compensated"];
    private static readonly HashSet<(string, string)> Edges =
    [ ("Pending", "Reserved"), ("Pending", "Rejected"),
      ("Reserved", "Compensating"), ("Compensating", "Compensated") ];

    public async Task<OrderSubmission> GetOrCreateAsync(NewOrderSubmission request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ActorAccountId) || request.ActorAccountId.Length > 450
            || request.RequestKey == Guid.Empty || request.UserId <= 0 || request.RequestHash is null || request.RequestHash.Length != 64
            || request.RequestHash.Any(ch => !Uri.IsHexDigit(ch)) || string.IsNullOrWhiteSpace(request.SnapshotJson)
            || request.SnapshotJson.Length > 65536)
            throw new ArgumentException("Invalid order submission identity, fingerprint, or snapshot.");
        var existing = await FindAsync(request.ActorAccountId, request.RequestKey, ct);
        if (existing is not null) return Match(existing, request);
        var now = clock.GetUtcNow().UtcDateTime;
        var submission = new OrderSubmission { Id = Guid.NewGuid(), ActorAccountId = request.ActorAccountId,
            RequestKey = request.RequestKey, UserId = request.UserId, RequestHash = request.RequestHash.ToUpperInvariant(),
            SnapshotJson = request.SnapshotJson, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.OrderSubmissions.Add(submission);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // A same-key contender may have committed first. Never reuse failed tracked inserts.
            db.Entry(submission).State = EntityState.Detached;
            existing = await FindAsync(request.ActorAccountId, request.RequestKey, ct);
            if (existing is null) throw;
            return Match(existing, request);
        }
        db.Entry(submission).State = EntityState.Detached;
        return submission;
    }
    public Task<OrderSubmission?> GetOwnedAsync(Guid id, string actorAccountId, CancellationToken ct = default)
        => db.OrderSubmissions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.ActorAccountId == actorAccountId, ct);

    public async Task<OrderSubmission?> TryAcquireAsync(Guid id, Guid leaseOwner, TimeSpan duration, CancellationToken ct = default)
    {
        if (leaseOwner == Guid.Empty || duration < TimeSpan.FromSeconds(10) || duration > TimeSpan.FromMinutes(5))
            throw new ArgumentException("A lease requires an owner and duration between 10 seconds and 5 minutes.");
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.Add(duration);
        var count = await db.OrderSubmissions.Where(row => row.Id == id && !Terminal.Contains(row.State)
                && (row.LeaseOwner == null || row.LeaseUntilUtc <= now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LeaseOwner, leaseOwner)
                .SetProperty(row => row.LeaseUntilUtc, expires).SetProperty(row => row.UpdatedAtUtc, now)
                .SetProperty(row => row.Version, row => row.Version + 1), ct);
        return count == 1 ? await db.OrderSubmissions.AsNoTracking().SingleAsync(row => row.Id == id, ct) : null;
    }
    public Task<bool> TransitionAsync(Guid id, Guid leaseOwner, string fromState, string toState, CancellationToken ct = default)
    {
        // Completion must be implemented with order insertion in ONE transaction later.
        // This generic store deliberately cannot mark a submission Completed on its own.
        if (!Edges.Contains((fromState, toState))) throw new ArgumentException("Unsupported submission transition.");
        return TransitionCoreAsync(id, leaseOwner, fromState, toState, ct);
    }
    private async Task<bool> TransitionCoreAsync(Guid id, Guid leaseOwner, string fromState, string toState, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.OrderSubmissions.Where(row => row.Id == id && row.LeaseOwner == leaseOwner
                && row.LeaseUntilUtc > now && row.State == fromState)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, toState)
                .SetProperty(row => row.UpdatedAtUtc, now).SetProperty(row => row.Version, row => row.Version + 1), ct) == 1;
    }
    public async Task<bool> ReleaseLeaseAsync(Guid id, Guid leaseOwner, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.OrderSubmissions.Where(row => row.Id == id && row.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LeaseOwner, (Guid?)null)
                .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null).SetProperty(row => row.UpdatedAtUtc, now)
                .SetProperty(row => row.Version, row => row.Version + 1), ct) == 1;
    }
    private Task<OrderSubmission?> FindAsync(string actor, Guid key, CancellationToken ct)
        => db.OrderSubmissions.AsNoTracking().SingleOrDefaultAsync(row => row.ActorAccountId == actor && row.RequestKey == key, ct);
    private static OrderSubmission Match(OrderSubmission existing, NewOrderSubmission request)
    {
        if (existing.UserId != request.UserId || !string.Equals(existing.RequestHash, request.RequestHash, StringComparison.OrdinalIgnoreCase))
            throw new DataConflictException("This request key already belongs to a different order payload.");
        return existing; // Keep the FIRST immutable price/item snapshot, not today's prices.
    }
}
