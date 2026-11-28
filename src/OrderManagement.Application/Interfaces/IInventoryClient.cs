namespace OrderManagement.Application.Interfaces;

public record InventoryReservationItem(long ProductId, int Quantity);
public record InventoryReservation(Guid ReservationId, string Status, IReadOnlyList<InventoryReservationItem> Items);

public interface IInventoryClient
{
    Task<InventoryReservation> ReserveAsync(Guid reservationId, IReadOnlyList<InventoryReservationItem> items, CancellationToken cancellationToken = default);
    Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<InventoryReservation> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default);
}
