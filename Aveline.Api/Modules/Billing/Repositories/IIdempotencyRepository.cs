using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>Persistence for the idempotency replay store (domain-model.md §4.3).</summary>
public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> FindAsync(
        Guid? organizationId, string endpoint, string httpMethod, string idempotencyKey,
        CancellationToken cancellationToken = default);

    void Add(IdempotencyRecord record);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default);
}
