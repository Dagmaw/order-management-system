using Moq;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Entities;
using Xunit;

namespace OrderManagement.Application.Tests;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IProductRepository> _products = new();

    private OrderService CreateService()
    {
        return new OrderService(
            _orders.Object,
            _users.Object,
            _products.Object);
    }

    private static CreateOrderDto ValidRequest()
    {
        return new CreateOrderDto
        {
            UserId = 1,
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = 10, Quantity = 2 }
            }
        };
    }

    private void ArrangeUser()
    {
        _users
            .Setup(repository => repository.GetByIdAsync(
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1 });
    }

    private void VerifyNothingSaved()
    {
        _orders.Verify(
            repository => repository.AddAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrder_CalculatesTotalAndSnapshotsPrices()
    {
        ArrangeUser();

        var product = new Product { Id = 10, Price = 25m };

        _products
            .Setup(repository => repository.GetByIdAsync(
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _products
            .Setup(repository => repository.GetByIdAsync(
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { Id = 20, Price = 10m });

        Order? savedOrder = null;

        _orders
            .Setup(repository => repository.AddAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order order, CancellationToken _) =>
            {
                savedOrder = order;
                order.Id = 100;
                return order;
            });

        var request = ValidRequest();
        request.Items.Add(
            new CreateOrderItemDto { ProductId = 20, Quantity = 3 });

        var result = await CreateService().CreateOrderAsync(request);

        // 2 × 25 + 3 × 10 = 80
        Assert.Equal(100, result.Id);
        Assert.Equal(80m, result.TotalAmount);
        Assert.Equal(2, result.Items.Count);

        // A later catalog change does not change the order snapshot.
        product.Price = 99m;

        Assert.NotNull(savedOrder);
        Assert.Equal(
            25m,
            savedOrder.Items.Single(item => item.ProductId == 10).UnitPrice);

        Assert.Equal(
            50m,
            result.Items.Single(item => item.ProductId == 10).LineTotal);

        _orders.Verify(
            repository => repository.AddAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task CreateOrder_InvalidQuantity_DoesNotSave(int quantity)
    {
        var request = ValidRequest();
        request.Items[0].Quantity = quantity;

        await Assert.ThrowsAsync<OrderValidationException>(
            () => CreateService().CreateOrderAsync(request));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task CreateOrder_DuplicateProducts_DoesNotSave()
    {
        var request = ValidRequest();

        request.Items.Add(
            new CreateOrderItemDto { ProductId = 10, Quantity = 1 });

        await Assert.ThrowsAsync<OrderValidationException>(
            () => CreateService().CreateOrderAsync(request));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task CreateOrder_MissingUser_DoesNotSave()
    {
        _users
            .Setup(repository => repository.GetByIdAsync(
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<OrderValidationException>(
            () => CreateService().CreateOrderAsync(ValidRequest()));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task CreateOrder_MissingProduct_DoesNotSave()
    {
        ArrangeUser();

        _products
            .Setup(repository => repository.GetByIdAsync(
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        await Assert.ThrowsAsync<OrderValidationException>(
            () => CreateService().CreateOrderAsync(ValidRequest()));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_DoesNotSave()
    {
        var request = ValidRequest();
        request.Items.Clear();

        await Assert.ThrowsAsync<OrderValidationException>(
            () => CreateService().CreateOrderAsync(request));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task CreateOrder_TotalExceedsDatabaseLimit_DoesNotSave()
    {
        ArrangeUser();

        _products
            .Setup(repository => repository.GetByIdAsync(
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product
            {
                Id = 10,
                Price = 9999999999999999.99m
            });

        await Assert.ThrowsAsync<OrderValidationException>(
            () => CreateService().CreateOrderAsync(ValidRequest()));

        VerifyNothingSaved();
    }
}