using OrderManagement.Infrastructure.Inventory;
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
    private readonly OrderSubmissionCoordinator? _submissions;

    public OrderController(IOrderService orderService, OrderSubmissionCoordinator? submissions = null)
    {
        _orderService = orderService;
        _submissions = submissions;
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
        CancellationToken cancellationToken,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
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

        if (_submissions is not null)
        {
            if (!Guid.TryParse(idempotencyKey, out var key) || key == Guid.Empty)
                return Problem(statusCode:400,title:"Idempotency-Key required",detail:"Provide one UUID for this logical order and reuse it for retries.");
            var actor = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(actor)) return Forbid();
            var submission = await _submissions.SubmitAsync(actor,key,serviceDto,cancellationToken);
            if (submission.State == "Completed" && submission.OrderId is int completedId)
                return CreatedAtRoute("GetOrderById",new { id=completedId },await _orderService.GetOrderByIdAsync(completedId,cancellationToken));
            if (submission.State is "Compensated" or "Rejected") return Conflict(submission);
            return AcceptedAtAction(nameof(GetSubmissionAsync),new { id=submission.Id },submission);
        }

        var order = await _orderService.CreateOrderAsync(
            serviceDto,
            cancellationToken);

        return CreatedAtRoute(
            "GetOrderById",
            new { id = order.Id },
            order);
    }

    [HttpGet("submissions/{id:guid}")]
    [ActionName(nameof(GetSubmissionAsync))]
    public async Task<IActionResult> GetSubmissionAsync(Guid id,CancellationToken cancellationToken)
    {
        if (_submissions is null) return NotFound();
        var actor=User.FindFirst("sub")?.Value;
        if(string.IsNullOrWhiteSpace(actor)) return Forbid();
        var result=await _submissions.GetOwnedAsync(id,actor,cancellationToken);
        return result is null?NotFound():Ok(result);
    }
}