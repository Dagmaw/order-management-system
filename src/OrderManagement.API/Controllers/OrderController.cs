using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.Interfaces;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.API.Controllers
{
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
        public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetOrdersAsync(CancellationToken cancellationToken)
        {
            var orders = await _orderService.GetOrdersAsync(cancellationToken);
            return Ok(orders);
        }
        [HttpGet("{id:int}", Name = "GetOrderById")]
        public async Task<ActionResult<OrderDto>> GetOrderByIAsync(int id, CancellationToken cancellationToken)
        {
            var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
            if (order == null)
            {
                return NotFound();
            }
            return Ok(order);
        }
        [HttpPost]
        public async Task<ActionResult<OrderDto>> CreateOrderAsync([FromBody] CreateOrderDto dto, CancellationToken cancellationToken)
        {
            try
            {
                var order = await _orderService.CreateOrderAsync(dto, cancellationToken);
                return CreatedAtRoute("GetOrderById", new { id = order.Id }, order);
            }
            catch (OrderValidationException ex)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid order",
                    detail: ex.Message);
            }
            catch (DataConflictException ex)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Order could not be saved",
                    detail: ex.Message);
            }
        }
    }
}
