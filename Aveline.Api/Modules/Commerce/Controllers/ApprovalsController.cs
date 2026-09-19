using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationConfiguration.BoutiqueAccessPolicy)]
[Route("api/v1/orgs/{organizationId:guid}/approvals")]
public class ApprovalsController : ControllerBase
{
    private readonly IApprovalService _approvalService;

    public ApprovalsController(IApprovalService approvalService)
    {
        _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ApprovalQueueResponseDto>>> GetAll(
        [FromRoute] Guid organizationId,
        [FromQuery] ApprovalQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _approvalService.GetPendingApprovalsAsync(organizationId, query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApprovalQueueResponseDto>> GetById(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var result = await _approvalService.GetApprovalByIdAsync(id, organizationId, ct);
        return result is not null ? Ok(result) : NotFound(new { message = $"Approval entry '{id}' not found." });
    }

    [HttpPost("{id:guid}/decision")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueApprovalDecisionPolicy)]
    public async Task<ActionResult<ApprovalQueueResponseDto>> ProcessDecision(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] ApprovalDecisionDto dto,
        CancellationToken ct = default)
    {
        try
        {
            Guid? userId = GetCurrentUserId();
            var result = await _approvalService.ProcessDecisionAsync(id, organizationId, dto, userId, ct);
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

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueApprovalDecisionPolicy)]
    public Task<ActionResult<ApprovalQueueResponseDto>> Approve(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] ApprovalDecisionDto? dto = null,
        CancellationToken ct = default)
    {
        var decisionDto = dto ?? new ApprovalDecisionDto();
        decisionDto.Decision = "approve";
        return ProcessDecision(organizationId, id, decisionDto, ct);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueApprovalDecisionPolicy)]
    public Task<ActionResult<ApprovalQueueResponseDto>> Reject(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] ApprovalDecisionDto? dto = null,
        CancellationToken ct = default)
    {
        var decisionDto = dto ?? new ApprovalDecisionDto();
        decisionDto.Decision = "reject";
        return ProcessDecision(organizationId, id, decisionDto, ct);
    }

    [HttpPost("{id:guid}/revise")]
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueApprovalDecisionPolicy)]
    public Task<ActionResult<ApprovalQueueResponseDto>> Revise(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] ApprovalDecisionDto dto,
        CancellationToken ct = default)
    {
        dto.Decision = "revise";
        return ProcessDecision(organizationId, id, dto, ct);
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return !string.IsNullOrEmpty(sub) && Guid.TryParse(sub, out var parsed) ? parsed : null;
    }
}
