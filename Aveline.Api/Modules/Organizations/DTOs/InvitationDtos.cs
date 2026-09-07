namespace Aveline.Api.Modules.Organizations.DTOs;

/// <summary>Request to invite a staff member (role + optional recipient email).</summary>
public record CreateInvitationRequest(string BoutiqueRole, string? RecipientEmail = null);

/// <summary>Result of creating an invitation — one-time code plus a shareable link.</summary>
public record CreateInvitationResponse(
    Guid InvitationId,
    string Code,
    string Link,
    string BoutiqueRole,
    string? RecipientEmail,
    DateTime ExpiresAt);

/// <summary>Non-secret view of a pending invitation.</summary>
public record PendingInvitationDto(
    Guid InvitationId,
    string BoutiqueRole,
    string? RecipientEmail,
    DateTime CreatedAt,
    DateTime ExpiresAt);
