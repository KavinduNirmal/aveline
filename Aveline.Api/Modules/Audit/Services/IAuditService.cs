using Aveline.Api.Modules.Audit.Models;

namespace Aveline.Api.Modules.Audit.Services;

/// <summary>Input to <see cref="IAuditService.RecordAsync"/>.</summary>
public sealed record AuditEntryRequest(
    string Action,
    string EntityType,
    string EntityId,
    Guid? OrganizationId = null,
    string ActorKind = AuditActorKind.System,
    Guid? ActorUserId = null,
    Guid? ActorApiKeyId = null,
    string? ActorRef = null,
    object? Before = null,
    object? After = null,
    string? Reason = null,
    string? RequestId = null,
    Guid? TraceId = null,
    string? IpHash = null,
    string? UserAgent = null);

/// <summary>
/// Writes audit entries. A failure to write a non-critical audit row is logged but never
/// propagated to the caller; the one exception (billing adjustments) arrives in Phase 2
/// and will opt into a transactional write instead.
/// </summary>
public interface IAuditService
{
    Task RecordAsync(AuditEntryRequest request, CancellationToken cancellationToken = default);
}
