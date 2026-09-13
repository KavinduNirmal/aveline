namespace Aveline.Api.Modules.Audit.Models;

/// <summary>
/// Append-only audit record (domain-model.md §9). One generic table serves every
/// module so cross-module audit queries are possible.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning organization, or <c>null</c> for system-level actions.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>One of <see cref="AuditActorKind"/>.</summary>
    public string ActorKind { get; set; } = AuditActorKind.System;

    public Guid? ActorUserId { get; set; }

    public Guid? ActorApiKeyId { get; set; }

    /// <summary>Clerk id or <c>internal-service</c> when no local user row exists.</summary>
    public string? ActorRef { get; set; }

    /// <summary>Dot-namespaced action, e.g. <c>pricing.rule.activated</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>Redacted snapshot before the change; JSON stored as <c>jsonb</c>.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>Redacted snapshot after the change; JSON stored as <c>jsonb</c>.</summary>
    public string? AfterJson { get; set; }

    public string? Reason { get; set; }

    public string? RequestId { get; set; }

    public Guid? TraceId { get; set; }

    /// <summary>SHA-256 hash of the client IP; never the raw address.</summary>
    public string? IpHash { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
