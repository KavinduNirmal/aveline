using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Organizations.Models;

/// <summary>
/// A code/link invitation to join an <see cref="Organization"/> as a member.
/// Only the <see cref="TokenHash"/> of the secret code is stored — the plaintext
/// code is returned to the inviter once and never persisted or logged.
/// Acceptance is one-time (see <see cref="AcceptedAt"/>).
/// </summary>
public class OrganizationInvitation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>The organization member who created the invitation.</summary>
    public Guid? InvitedByUserId { get; set; }

    /// <summary>Recipient when the invitation targets an existing user.</summary>
    public Guid? RecipientUserId { get; set; }

    /// <summary>Recipient when the invitation is sent to an email address.</summary>
    public string? RecipientEmail { get; set; }

    /// <summary>SHA-256 hash of the secret invitation code.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Canonical boutique role granted on acceptance (an <c>org:boutique_*</c> role).</summary>
    public string BoutiqueRole { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Organization? Organization { get; set; }

    public User? InvitedBy { get; set; }

    public User? Recipient { get; set; }

    /// <summary>True when the invitation can still be accepted.</summary>
    public bool IsAcceptable(DateTime utcNow) =>
        AcceptedAt is null
        && RevokedAt is null
        && ExpiresAt > utcNow;
}
