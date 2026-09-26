namespace Aveline.Api.Modules.Organizations.DTOs;

/// <summary>
/// The bounds on invitation creation, stated once so the single route and the bulk route cannot
/// disagree about them.
/// </summary>
/// <remarks>
/// **`count` is the control and the rate limiter is the backstop.** Before T6 the only bound on a
/// bulk request was whatever the client sent, and there was no limiter on creation at all (only on
/// <c>POST /invitations/accept</c>). The clamp is applied server-side rather than validated-and-
/// rejected because a caller asking for 50 codes and receiving 10 is a smaller surprise than a 400
/// in the middle of onboarding a shop; the response reports both the requested and the created count
/// so the clamp is visible rather than silent.
///
/// `validityHours` is bounded for the same reason the client already offers 24/168/720: a one-hour
/// code and a thirty-day code are both legitimate, a negative one is not, and an unbounded value
/// would let a caller mint a code that never expires.
/// </remarks>
public static class InvitationLimits
{
    public const int MinValidityHours = 1;
    public const int MaxValidityHours = 720;
    public const int DefaultValidityHours = 24;

    public const int MinBulkCount = 1;
    public const int MaxBulkCount = 10;

    public static int ClampValidityHours(int? requested)
        => Math.Clamp(requested ?? DefaultValidityHours, MinValidityHours, MaxValidityHours);

    public static int ClampBulkCount(int requested)
        => Math.Clamp(requested, MinBulkCount, MaxBulkCount);
}

/// <summary>
/// The status of the optional summary notice to the acting owner.
/// </summary>
/// <remarks>
/// It is a three-valued status rather than a boolean because "not asked for" and "asked for but not
/// sent" are different facts, and collapsing them is how a silently dropped notification happens.
/// `Dispatched` means the notice was handed to the configured <c>IEmailService</c>; the repository's
/// implementation records a dispatch rather than a delivery, and the note says so.
/// </remarks>
public static class InvitationSummaryEmailStatus
{
    public const string NotRequested = "NotRequested";
    public const string Dispatched = "Dispatched";
    public const string NotSent = "NotSent";
}

/// <summary>Request to invite a staff member (role, optional recipient email and validity).</summary>
/// <remarks>
/// <paramref name="ValidityHours"/> and <paramref name="SendSummaryToOwner"/> were sent by the tenant
/// panel and silently dropped before T6, because the request record did not declare them (F-4).
/// </remarks>
public record CreateInvitationRequest(
    string BoutiqueRole,
    string? RecipientEmail = null,
    int? ValidityHours = null,
    bool SendSummaryToOwner = false);

/// <summary>Request to mint several invitation codes for one role in a single call (E-10).</summary>
public record BulkCreateInvitationRequest(
    string BoutiqueRole,
    int Count,
    int? ValidityHours = null,
    bool SendSummaryToOwner = false);

/// <summary>Result of creating an invitation — one-time code plus shareable links.</summary>
/// <remarks>
/// The three summary-email fields are always present so a caller can render the outcome without
/// inferring it from the absence of an error, which is exactly how the previous version dropped the
/// request.
/// </remarks>
public record CreateInvitationResponse(
    Guid InvitationId,
    string Code,
    string Link,
    string MobileLink,
    string BoutiqueRole,
    string? RecipientEmail,
    DateTime ExpiresAt,
    bool SummaryEmailRequested,
    string SummaryEmailStatus,
    string? SummaryEmailNote);

/// <summary>Result of a bulk creation: the codes plus what the clamp and the notice actually did.</summary>
public record BulkCreateInvitationResponse(
    IReadOnlyList<CreateInvitationResponse> Invitations,
    int RequestedCount,
    int CreatedCount,
    int EffectiveValidityHours,
    bool SummaryEmailRequested,
    string SummaryEmailStatus,
    string? SummaryEmailNote);

/// <summary>Non-secret view of a pending invitation.</summary>
public record PendingInvitationDto(
    Guid InvitationId,
    string BoutiqueRole,
    string? RecipientEmail,
    DateTime CreatedAt,
    DateTime ExpiresAt);
