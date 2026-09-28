using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Payments.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Payments.Repositories;

/// <summary>EF Core implementation of <see cref="IPaymentProviderEventRepository"/>.</summary>
internal sealed class PaymentProviderEventRepository(AppDbContext db) : IPaymentProviderEventRepository
{
    public Task<PaymentProviderEvent?> FindByProviderEventIdAsync(
        string provider, string providerEventId, CancellationToken cancellationToken = default) =>
        db.PaymentProviderEvents.FirstOrDefaultAsync(
            providerEvent => providerEvent.Provider == provider
                && providerEvent.ProviderEventId == providerEventId,
            cancellationToken);

    public async Task<IReadOnlyList<PaymentProviderEvent>> ListUnprocessedAsync(
        int limit, CancellationToken cancellationToken = default) =>
        await db.PaymentProviderEvents
            .Where(providerEvent => providerEvent.ProcessedAt == null)
            .OrderBy(providerEvent => providerEvent.ReceivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(
        PaymentProviderEvent providerEvent, CancellationToken cancellationToken = default)
    {
        db.PaymentProviderEvents.Add(providerEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        PaymentProviderEvent providerEvent, CancellationToken cancellationToken = default)
    {
        db.PaymentProviderEvents.Update(providerEvent);
        await db.SaveChangesAsync(cancellationToken);
    }
}
