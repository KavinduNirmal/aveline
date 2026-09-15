using Aveline.Api.Modules.Audit.Models;

namespace Aveline.Api.Modules.Audit.Repositories;

/// <summary>
/// Append-only audit store. Deliberately exposes no update or delete operation so the
/// audit guarantee cannot be broken by a future caller (domain-model.md C-1). The read
/// methods added for #241 are the only way the log becomes visible outside the process.
/// </summary>
public interface IAuditRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Reads a single entry, or <c>null</c> when it does not exist.</summary>
    Task<AuditLogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries entries newest-first. Every filter is optional; a blank string is ignored.
    /// Returns the page items and the unpaged total for the same filters.
    /// </summary>
    Task<(IReadOnlyList<AuditLogEntry> Items, int Total)> QueryAsync(
        string? action,
        string? entityType,
        string? entityId,
        Guid? organizationId,
        Guid? actorUserId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
