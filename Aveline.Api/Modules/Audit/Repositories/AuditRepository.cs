using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Audit.Repositories;

/// <summary>EF Core implementation of the append-only audit store.</summary>
public sealed class AuditRepository(AppDbContext db) : IAuditRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuditLogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<AuditLogEntry> Items, int Total)> QueryAsync(
        string? action,
        string? entityType,
        string? entityId,
        Guid? organizationId,
        Guid? actorUserId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var actionValue = action.Trim();
            query = query.Where(entry => entry.Action == actionValue);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var entityTypeValue = entityType.Trim();
            query = query.Where(entry => entry.EntityType == entityTypeValue);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var entityIdValue = entityId.Trim();
            query = query.Where(entry => entry.EntityId == entityIdValue);
        }

        if (organizationId is not null)
        {
            query = query.Where(entry => entry.OrganizationId == organizationId);
        }

        if (actorUserId is not null)
        {
            query = query.Where(entry => entry.ActorUserId == actorUserId);
        }

        if (from is not null)
        {
            query = query.Where(entry => entry.CreatedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(entry => entry.CreatedAt <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
