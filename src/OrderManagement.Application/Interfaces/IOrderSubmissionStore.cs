using OrderManagement.Domain.Entities;
namespace OrderManagement.Application.Interfaces;

public record NewOrderSubmission(string ActorAccountId, Guid RequestKey, int UserId,
    string RequestHash, string SnapshotJson);

public interface IOrderSubmissionStore
{
    Task<OrderSubmission> GetOrCreateAsync(NewOrderSubmission request, CancellationToken ct = default);
    Task<OrderSubmission?> GetOwnedAsync(Guid id, string actorAccountId, CancellationToken ct = default);
    Task<OrderSubmission?> TryAcquireAsync(Guid id, Guid leaseOwner, TimeSpan duration, CancellationToken ct = default);
    Task<bool> TransitionAsync(Guid id, Guid leaseOwner, string fromState, string toState, CancellationToken ct = default);
    Task<bool> ReleaseLeaseAsync(Guid id, Guid leaseOwner, CancellationToken ct = default);
}
