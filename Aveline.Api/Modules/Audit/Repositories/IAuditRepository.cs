using Aveline.Api.Modules.Audit.Models;

namespace Aveline.Api.Modules.Audit.Repositories;

/// <summary>
/// Append-only audit store. Deliberately exposes no update or delete operation so the
/// audit guarantee cannot be broken by a future caller (domain-model.md C-1).
/// </summary>
public interface IAuditRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
