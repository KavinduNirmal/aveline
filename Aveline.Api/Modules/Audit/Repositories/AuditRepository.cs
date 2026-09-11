using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;

namespace Aveline.Api.Modules.Audit.Repositories;

/// <summary>EF Core implementation of the append-only audit store.</summary>
public sealed class AuditRepository(AppDbContext db) : IAuditRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
