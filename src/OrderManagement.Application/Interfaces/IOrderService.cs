using OrderManagement.Application.DTOs;

namespace OrderManagement.Application.Interfaces
{
    public interface IOrderService
    {
        Task<OrderManagement.Domain.Entities.Order> PrepareOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default);
        Task<OrderDto> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default);

        Task<OrderDto?> GetOrderByIdAsync(int Id, CancellationToken cancellationToken = default);
        
        Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<OrderDto>> GetOrdersByUserIdAsync(
            int userId,
            CancellationToken cancellationToken = default);
    }
}
