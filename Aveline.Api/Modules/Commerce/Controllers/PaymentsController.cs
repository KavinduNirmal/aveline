using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationConfiguration.BoutiqueAccessPolicy)]
[Route("api/v1/orgs/{organizationId:guid}/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
    }

    [HttpPost]
    public async Task<ActionResult<PaymentResponseDto>> GeneratePayment(
        [FromRoute] Guid organizationId,
        [FromBody] GeneratePaymentRequestDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _paymentService.GeneratePaymentRequestAsync(organizationId, dto, ct);
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
    public async Task<ActionResult<PaymentResponseDto>> GetById(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var result = await _paymentService.GetPaymentByIdAsync(id, organizationId, ct);
        return result is not null ? Ok(result) : NotFound(new { message = $"Payment '{id}' not found." });
    }

    [HttpGet("order/{orderId:guid}")]
    public async Task<ActionResult<PaymentResponseDto>> GetByOrderId(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid orderId,
        CancellationToken ct = default)
    {
        var result = await _paymentService.GetPaymentByOrderIdAsync(orderId, organizationId, ct);
        return result is not null ? Ok(result) : NotFound(new { message = $"No payment found for order '{orderId}'." });
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<PaymentResponseDto>> Confirm(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] ConfirmPaymentDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _paymentService.ConfirmPaymentAsync(organizationId, id, dto, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiquePaymentRefundPolicy)]
    public async Task<ActionResult<PaymentResponseDto>> Refund(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _paymentService.RefundPaymentAsync(organizationId, id, null, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PaymentResponseDto>>> List(
        [FromRoute] Guid organizationId,
        [FromQuery] PaymentQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _paymentService.ListPaymentsAsync(organizationId, query, ct);
        return Ok(result);
    }
}
