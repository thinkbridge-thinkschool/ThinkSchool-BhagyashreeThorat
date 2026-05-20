using Microsoft.AspNetCore.Mvc;
using RefactorApi.DTOs;
using RefactorApi.Services.Interfaces;

namespace RefactorApi.Controllers;

[ApiController]
[Route("api/orders")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        IOrderService orderService,
        ILogger<OrderController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CreateOrderResponseDto>> CreateOrder(
        [FromBody] CreateOrderRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _orderService
                .CreateOrderAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Validation error");

            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error");

            return StatusCode(500, "Internal server error");
        }
    }
}