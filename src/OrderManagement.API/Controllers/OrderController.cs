using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.API.Security;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;

namespace OrderManagement.API.Controllers;

[Authorize(Roles = "Customer,Admin")]
[ApiController]
[Route("api/orders")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetOrdersAsync(
        CancellationToken cancellationToken)
    {
        if (User.IsInRole("Admin"))
        {
            var allOrders = await _orderService.GetOrdersAsync(
                cancellationToken);

            return Ok(allOrders);
        }

        if (!User.TryGetDomainUserId(out var currentUserId))
        {
            return Forbid();
        }

        var ownOrders = await _orderService.GetOrdersByUserIdAsync(
            currentUserId,
            cancellationToken);

        return Ok(ownOrders);
    }

    [HttpGet("{id:int}", Name = "GetOrderById")]
    public async Task<ActionResult<OrderDto>> GetOrderByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(
            id,
            cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (!User.IsInRole("Admin"))
        {
            if (!User.TryGetDomainUserId(out var currentUserId))
            {
                return Forbid();
            }

            if (order.UserId != currentUserId)
            {
                return NotFound();
            }
        }

        return Ok(order);
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrderAsync(
        [FromBody] PlaceOrderDto dto,
        CancellationToken cancellationToken)
    {
        int orderUserId;

        if (User.IsInRole("Admin"))
        {
            if (!dto.UserId.HasValue)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "User ID required",
                    detail: "Administrators must specify the order's User ID.");
            }

            orderUserId = dto.UserId.Value;
        }
        else
        {
            if (!User.TryGetDomainUserId(out var currentUserId))
            {
                return Forbid();
            }

            if (dto.UserId.HasValue &&
                dto.UserId.Value != currentUserId)
            {
                return Forbid();
            }

            orderUserId = currentUserId;
        }

        var serviceDto = new CreateOrderDto
        {
            UserId = orderUserId,
            Items = dto.Items
        };

        var order = await _orderService.CreateOrderAsync(
            serviceDto,
            cancellationToken);

        return CreatedAtRoute(
            "GetOrderById",
            new { id = order.Id },
            order);
    }
}