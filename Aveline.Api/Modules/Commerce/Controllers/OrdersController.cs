using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

[ApiController]
[Route("api/v1/orgs/{orgId:guid}/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponseDto>>> GetOrders(
        [FromRoute] Guid orgId,
        [FromQuery] OrderQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _orderService.GetOrdersAsync(orgId, query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponseDto>> GetById(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var order = await _orderService.GetOrderByIdAsync(id, orgId, ct);
        return order is not null ? Ok(order) : NotFound();
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrderResponseDto>> Update(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        [FromBody] CreateOrderDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await _orderService.UpdateOrderAsync(id, orgId, dto, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponseDto>> Create(
        [FromRoute] Guid orgId,
        [FromBody] CreateOrderDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var created = await _orderService.CreateOrderAsync(orgId, dto, null, ct);
            return CreatedAtAction(nameof(GetById), new { orgId, id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderResponseDto>> UpdateStatus(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        [FromBody] UpdateOrderStatusDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await _orderService.TransitionStatusAsync(id, orgId, dto, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        [FromQuery] string? reason = null,
        CancellationToken ct = default)
    {
        try
        {
            var cancelled = await _orderService.CancelOrderAsync(id, orgId, reason, ct);
            return cancelled ? Ok(new { success = true, message = "Order successfully cancelled." }) : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/recalculate")]
    public async Task<ActionResult<OrderResponseDto>> Recalculate(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        try
        {
            var recalculated = await _orderService.RecalculateOrderAsync(id, orgId, ct);
            return Ok(recalculated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
