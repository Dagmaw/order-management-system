namespace OrderManagement.Domain.Entities;

// Separate from Orders: a persisted attempt is not yet a successfully placed order.
public sealed class OrderSubmission
{
    public Guid Id { get; set; }
    public string ActorAccountId { get; set; } = "";
    public Guid RequestKey { get; set; }
    public int UserId { get; set; }
    public string RequestHash { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string State { get; set; } = "Pending";
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
