using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Interfaces
{
    public interface IOrderRepository
    {
        Task<Order?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Order>> GetAllAsync(
            CancellationToken cancellationToken = default);

        Task<Order> AddAsync(
            Order order,
            CancellationToken cancellationToken = default);
    }
}
