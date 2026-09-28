using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
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
    private readonly IUserRepository _users;
    private readonly IAuthorizationService _authorization;

    public ApprovalsController(
        IApprovalService approvalService,
        IUserRepository users,
        IAuthorizationService authorization)
    {
        _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
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
            // Q14: the **verb**, not only the route, decides the permission. `/decision` takes an
            // arbitrary decision string, so without this check a staff member holding
            // `approvals:approve` could post `{"decision":"reject"}` and cancel the order the split
            // exists to protect.
            if (ApprovalDecisions.RequiresOrderManage(dto.Decision))
            {
                var authorised = await _authorization.AuthorizeAsync(
                    User, resource: null, AuthorizationConfiguration.BoutiqueOrderManagePolicy);
                if (!authorised.Succeeded)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        code = "order-manage-required",
                        message = "Rejecting or revising an order requires order management permission.",
                    });
                }
            }

            var userId = await ResolveActorUserIdAsync(ct);
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
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
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
    [Authorize(Policy = AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
    public Task<ActionResult<ApprovalQueueResponseDto>> Revise(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] ApprovalDecisionDto dto,
        CancellationToken ct = default)
    {
        dto.Decision = "revise";
        return ProcessDecision(organizationId, id, dto, ct);
    }

    /// <summary>
    /// Resolves the acting user the way every other actor in the codebase is resolved: from the
    /// Clerk subject through <c>IUserRepository.GetByClerkIdAsync</c>.
    /// </summary>
    /// <remarks>
    /// The previous implementation called <c>Guid.TryParse</c> on the subject. A Clerk `sub` is a
    /// string such as <c>user_2abc…</c>, so the parse always failed and every decision was recorded
    /// with a null actor. The one passing test injected a GUID subject, which is why CI never
    /// caught it.
    /// </remarks>
    private async Task<Guid?> ResolveActorUserIdAsync(CancellationToken ct)
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
