using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationConfiguration.BoutiqueAccessPolicy)]
[Route("api/v1/orgs/{organizationId:guid}/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IUserRepository _users;

    public PaymentsController(IPaymentService paymentService, IUserRepository users)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
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
        [FromBody] RefundPaymentRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            // The ledger attributes a refund to the person who issued it, so the actor is resolved
            // here — through `IUserRepository`, the way every other actor in this codebase is, and
            // never by parsing the Clerk subject as a GUID.
            var actor = await ResolveActorAsync(ct);
            var result = await _paymentService.RefundPaymentAsync(
                organizationId, id, request?.Reason, ct, actor);
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

    /// <summary>
    /// Resolves the acting user from the Clerk subject through <c>IUserRepository</c>. A real
    /// subject is a string such as <c>user_2abc…</c>, so it is never parsed as a GUID.
    /// </summary>
    private async Task<Guid?> ResolveActorAsync(CancellationToken ct)
    {
        var clerkId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return null;
        }

        var user = await _users.GetByClerkIdAsync(clerkId, ct);
        return user?.Id;
    }
}
