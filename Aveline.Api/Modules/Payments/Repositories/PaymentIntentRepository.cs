using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Payments.Repositories;

/// <summary>EF Core implementation of <see cref="IPaymentIntentRepository"/>.</summary>
internal sealed class PaymentIntentRepository(AppDbContext db) : IPaymentIntentRepository
{
    public Task<PaymentIntent?> GetAsync(
        Guid organizationId, Guid intentId, CancellationToken cancellationToken = default) =>
        db.PaymentIntents.FirstOrDefaultAsync(
            intent => intent.OrganizationId == organizationId && intent.Id == intentId,
            cancellationToken);

    public Task<PaymentIntent?> FindByProviderIntentIdAsync(
        string provider, string providerIntentId, CancellationToken cancellationToken = default) =>
        db.PaymentIntents.FirstOrDefaultAsync(
            intent => intent.Provider == provider && intent.ProviderIntentId == providerIntentId,
            cancellationToken);

    public Task<PaymentIntent?> FindByIdempotencyKeyAsync(
        Guid organizationId, PaymentPurpose purpose, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        db.PaymentIntents.FirstOrDefaultAsync(
            intent => intent.OrganizationId == organizationId
                && intent.Purpose == purpose
                && intent.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task AddAsync(PaymentIntent intent, CancellationToken cancellationToken = default)
    {
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PaymentIntent intent, CancellationToken cancellationToken = default)
    {
        db.PaymentIntents.Update(intent);
        await db.SaveChangesAsync(cancellationToken);
    }
}
