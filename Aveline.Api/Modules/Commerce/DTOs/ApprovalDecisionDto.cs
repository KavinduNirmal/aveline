using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

/// <summary>
/// One approval decision. <see cref="Decision"/> is <b>not</b> a required body field, because three of
/// the four routes put the verb in the URL and must be able to lend it to this type.
/// </summary>
/// <remarks>
/// <para>
/// <c>[Required]</c> used to sit on <see cref="Decision"/>, and with <c>[ApiController]</c>'s
/// automatic model validation that made every verb route unusable: the dashboard posts
/// <c>POST /approvals/{id}/approve</c> with a body of <c>{reason, revisedDiscount}</c>, the request
/// was rejected with <c>400 "The Decision field is required."</c> before
/// <c>ApprovalsController.Approve</c> could set the verb itself. The attribute was redundant as well
/// as harmful — <c>ApprovalService.ProcessDecisionAsync</c> refuses a blank decision — and the tests
/// could not see it, because they call the action directly and never pass through model binding.
/// </para>
/// <para>
/// The requirement is therefore stated once, in the service, where it is true for the one route whose
/// body really is the source of the verb (<c>POST /approvals/{id}/decision</c>). Deciding whether a
/// decision is present is a domain rule about settling a pause, not a shape rule about a payload, and
/// the service is the boundary that must hold it whichever route was used.
/// </para>
/// </remarks>
public class ApprovalDecisionDto
{
    /// <summary>One of <c>approve</c> | <c>reject</c> | <c>revise</c>. Supplied by the route on the verb routes.</summary>
    [MaxLength(32)]
    public string Decision { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Reason { get; set; }

    public decimal? RevisedDiscount { get; set; }
}
