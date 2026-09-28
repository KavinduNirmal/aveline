using Aveline.Api.Common.MultiTenancy;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// Append-only consent history (plan §3.2). The single <see cref="CustomerConsent"/> row holds the
/// effective state; this table is the source of truth for how that state was reached, because the
/// two timestamp scalars on the consent row are lossy by construction.
///
/// No message content is ever stored here: <see cref="EvidenceJson"/> carries identifiers only
/// (an OTP attempt id, a link id, a disclosure version).
/// </summary>
public class ConsentAuditEntry : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>One of the consent/privacy constants on <c>Modules.Audit.Models.AuditAction</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The effective status before the change; null when the row was created.</summary>
    public string? PreviousStatus { get; set; }

    public string NewStatus { get; set; } = string.Empty;

    /// <summary>
    /// How the status was reached: <c>otp_link</c> | <c>staff</c> | <c>api</c> | <c>system</c> |
    /// <c>welcome_message</c>.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Who acted: <c>customer</c> | <c>user</c> | <c>system</c> | <c>internal_service</c>.
    /// </summary>
    public string ActorKind { get; set; } = string.Empty;

    /// <summary>FK to <c>Users</c> when a staff member acted; null for a customer or system act.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>A hashed phone or a clerk id; never a raw identifier.</summary>
    public string? ActorRef { get; set; }

    /// <summary>
    /// Identifiers only - never message content. Stored as <c>jsonb</c> with an empty-object
    /// default, so a reader never has to distinguish "no evidence" from "not an object".
    /// </summary>
    public string EvidenceJson { get; set; } = "{}";

    /// <summary>SHA-256 hash of the client IP; never the raw address (matches AuditLogEntry).</summary>
    public string? IpHash { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
