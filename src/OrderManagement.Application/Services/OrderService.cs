using OrderManagement.Application.Interfaces;
using OrderManagement.Application.DTOs;
using OrderManagement.Domain.Entities;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Application.Services
{
    public class OrderService : IOrderService
    {
        private const decimal MaximumAmount = 9999999999999999.99m;

        private readonly IOrderRepository _orderRepository;
        private readonly IUserRepository _userRepository;
        private readonly IProductRepository _productRepository;

        public OrderService(IOrderRepository orderRepository, IUserRepository userRepository, IProductRepository productRepository)
        {
            _orderRepository = orderRepository;
            _userRepository = userRepository;
            _productRepository = productRepository;
        }

        public async Task<OrderDto> CreateOrderAsync(CreateOrderDto dto,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (dto.UserId <= 0)
            {
                throw new OrderValidationException("A valid user ID is required.");
            }

            if (dto.Items is null || dto.Items.Count is < 1 or > 100)
            {
                throw new OrderValidationException(
                    "An order must contain between 1 and 100 items.");
            }

            if (dto.Items.Any(item =>
                    item is null
                    || item.ProductId <= 0
                    || item.Quantity is < 1 or > 1000))
            {
                throw new OrderValidationException(
                    "Each item requires a valid product ID and quantity from 1 to 1000.");
            }

            var hasDuplicates = dto.Items
                .GroupBy(item => item.ProductId)
                .Any(group => group.Count() > 1);

            if (hasDuplicates)
            {
                throw new OrderValidationException(
                    "Each product may appear only once in an order.");
            }

            var user = await _userRepository.GetByIdAsync(
                dto.UserId,
                cancellationToken);

            if (user is null)
            {
                throw new OrderValidationException("The selected user does not exist.");
            }

            var order = new Order
            {
                UserId = dto.UserId
            };

            foreach (var requestedItem in dto.Items)
            {
                var product = await _productRepository.GetByIdAsync(
                    requestedItem.ProductId,
                    cancellationToken);
                if (product is null)
                {
                    throw new OrderValidationException(
                        $"The product with ID {requestedItem.ProductId} does not exist.");
                }
                if (product.Price <= 0 || product.Price > MaximumAmount)
                {
                    throw new OrderValidationException(
                        $"The product with ID {requestedItem.ProductId} has an invalid price.");
                }

                var item = new OrderItem
                {
                    ProductId = product.Id,
                    Quantity = requestedItem.Quantity,
                    UnitPrice = product.Price
                };

                if (item.LineTotal > MaximumAmount - order.TotalAmount)
                {
                    throw new OrderValidationException(
                        $"The line total for product ID {requestedItem.ProductId} exceeds the maximum allowed amount.");
                }

                order.Items.Add(item);
                order.TotalAmount += item.LineTotal;
            }
            var SavedOrder = await _orderRepository.AddAsync(order, cancellationToken);

            return ToDto(SavedOrder);
        }

        public async Task<OrderDto?> GetOrderByIdAsync(int id,
            CancellationToken cancellationToken = default)
        {
            var order = await _orderRepository.GetByIdAsync(id, cancellationToken);
            return order is null ? null : ToDto(order);
        }

        public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default)
        {
            var orders = await
                _orderRepository.GetAllAsync(cancellationToken);

            return orders.Select(ToDto).ToList();
        }
        private static OrderDto ToDto(Order order)
        {
            return new OrderDto
            {
                Id = order.Id,
                UserId = order.UserId,
                CreatedAt = order.CreatedAt,
                TotalAmount = order.TotalAmount,
                Items = order.Items
                    .OrderBy(item => item.ProductId)
                    .Select(item => new OrderItemDto
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        LineTotal = item.LineTotal
                    })
                    .ToList()
            };
        }
    }
}
