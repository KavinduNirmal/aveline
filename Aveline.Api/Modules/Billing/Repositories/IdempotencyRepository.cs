using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>EF Core implementation of the idempotency replay store.</summary>
public sealed class IdempotencyRepository(AppDbContext db) : IIdempotencyRepository
{
    public Task<IdempotencyRecord?> FindAsync(
        Guid? organizationId, string endpoint, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        db.IdempotencyRecords.FirstOrDefaultAsync(
            record => record.OrganizationId == organizationId
                   && record.Endpoint == endpoint
                   && record.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public void Add(IdempotencyRecord record) => db.IdempotencyRecords.Add(record);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<int> DeleteExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            var expired = await db.IdempotencyRecords
                .Where(record => record.ExpiresAt <= asOf)
                .ToListAsync(cancellationToken);
            db.IdempotencyRecords.RemoveRange(expired);
            await db.SaveChangesAsync(cancellationToken);
            return expired.Count;
        }

        return await db.IdempotencyRecords
            .Where(record => record.ExpiresAt <= asOf)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
