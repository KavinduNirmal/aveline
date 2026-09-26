using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationConfiguration.BoutiqueAccessPolicy)]
[Route("api/v1/orgs/{organizationId:guid}/deliveries")]
public class DeliveriesController : ControllerBase
{
    private readonly IDeliveryService _deliveryService;

    public DeliveriesController(IDeliveryService deliveryService)
    {
        _deliveryService = deliveryService ?? throw new ArgumentNullException(nameof(deliveryService));
    }

    [HttpPost]
    public async Task<ActionResult<DeliveryPlanResponseDto>> CreateDelivery(
        [FromRoute] Guid organizationId,
        [FromBody] CreateDeliveryDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _deliveryService.CreateDeliveryPlanAsync(organizationId, dto, ct);
            return CreatedAtAction(nameof(GetById), new { organizationId, id = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeliveryPlanResponseDto>> GetById(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var result = await _deliveryService.GetDeliveryPlanByIdAsync(id, organizationId, ct);
        return result is not null ? Ok(result) : NotFound(new { message = $"Delivery plan '{id}' not found." });
    }

    [HttpGet("order/{orderId:guid}")]
    public async Task<ActionResult<DeliveryPlanResponseDto>> GetByOrderId(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid orderId,
        CancellationToken ct = default)
    {
        var result = await _deliveryService.GetDeliveryPlanByOrderIdAsync(orderId, organizationId, ct);
        return result is not null ? Ok(result) : NotFound(new { message = $"No delivery plan found for order '{orderId}'." });
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<DeliveryPlanResponseDto>> UpdateStatus(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] UpdateDeliveryStatusDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _deliveryService.UpdateDeliveryStatusAsync(organizationId, id, dto, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<DeliveryPlanResponseDto>>> List(
        [FromRoute] Guid organizationId,
        [FromQuery] DeliveryQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _deliveryService.ListDeliveryPlansAsync(organizationId, query, ct);
        return Ok(result);
    }
}
