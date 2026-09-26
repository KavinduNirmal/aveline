using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

/// <summary>
/// The order lifecycle, scoped to one organization.
/// </summary>
/// <remarks>
/// **The route token is <c>organizationId</c>, not <c>orgId</c>.** The organization-scope handler
/// matches <c>RouteValues["organizationId"]</c> exactly, so a policy on a route declared
/// <c>{orgId}</c> would never bind — which, combined with a missing attribute, is how this
/// controller was reachable by any authenticated caller in any tenant.
///
/// The policy is deliberately split (TD11):
/// <list type="bullet">
///   <item>reads and <c>POST</c> (create) take <see cref="AuthorizationConfiguration.BoutiqueMemberPolicy"/>
///   — a counter associate must see and create the order they are serving;</item>
///   <item>update, status change, cancel and recalculate take
///   <see cref="AuthorizationConfiguration.BoutiqueOrderManagePolicy"/> (<c>orders:manage</c>),
///   because Q8 says staff may approve a customer order but may not change its lifecycle.</item>
/// </list>
/// <c>BoutiqueAccess</c> (= <c>catalog:view</c>) is deliberately **not** used here: it is held by
/// every boutique role and would have handed staff the ability to cancel orders.
/// </remarks>
[ApiController]
[Route("api/v1/orgs/{organizationId:guid}/orders")]
[Authorize(Policy = AuthorizationConfiguration.BoutiqueMemberPolicy)]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponseDto>>> GetOrders(
        [FromRoute] Guid organizationId,
        [FromQuery] OrderQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _orderService.GetOrdersAsync(organizationId, query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponseDto>> GetById(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var order = await _orderService.GetOrderByIdAsync(id, organizationId, ct);
        return order is not null ? Ok(order) : NotFound();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
    public async Task<ActionResult<OrderResponseDto>> Update(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] CreateOrderDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await _orderService.UpdateOrderAsync(id, organizationId, dto, ct);
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
        [FromRoute] Guid organizationId,
        [FromBody] CreateOrderDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var created = await _orderService.CreateOrderAsync(organizationId, dto, null, ct);
            return CreatedAtAction(
                nameof(GetById), new { organizationId, id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
    public async Task<ActionResult<OrderResponseDto>> UpdateStatus(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] UpdateOrderStatusDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await _orderService.TransitionStatusAsync(id, organizationId, dto, ct);
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
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromQuery] string? reason = null,
        CancellationToken ct = default)
    {
        try
        {
            var cancelled = await _orderService.CancelOrderAsync(id, organizationId, reason, ct);
            return cancelled ? Ok(new { success = true, message = "Order successfully cancelled." }) : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/recalculate")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
    public async Task<ActionResult<OrderResponseDto>> Recalculate(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        try
        {
            var recalculated = await _orderService.RecalculateOrderAsync(id, organizationId, ct);
            return Ok(recalculated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
