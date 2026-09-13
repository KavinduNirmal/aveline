using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>EF Core implementation of <see cref="IEntitlementRepository"/>.</summary>
public sealed class EntitlementRepository(AppDbContext db) : IEntitlementRepository
{
    public async Task<PlanTier> GetOrganizationPlanTierAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var tier = await db.Organizations
            .Where(org => org.Id == organizationId)
            .Select(org => (PlanTier?)org.PlanTier)
            .FirstOrDefaultAsync(cancellationToken);

        return tier ?? PlanTier.Seed;
    }

    public async Task<IReadOnlyList<PlanEntitlement>> ListEffectivePlanEntitlementsAsync(
        PlanTier tier, DateTime at, CancellationToken cancellationToken = default)
    {
        return await db.PlanEntitlements
            .Where(e => e.PlanTier == tier)
            .Where(e => e.EffectiveFrom <= at)
            .Where(e => e.EffectiveTo == null || e.EffectiveTo > at)
            .OrderByDescending(e => e.EffectiveFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlanEntitlementOverride>> ListEffectiveOverridesAsync(
        Guid organizationId, DateTime at, CancellationToken cancellationToken = default)
    {
        return await db.PlanEntitlementOverrides
            .Where(o => o.OrganizationId == organizationId)
            .Where(o => o.EffectiveFrom <= at)
            .Where(o => o.EffectiveTo == null || o.EffectiveTo > at)
            .OrderByDescending(o => o.EffectiveFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task<PlanEntitlementOverride> UpsertOverrideAsync(
        PlanEntitlementOverride entry, CancellationToken cancellationToken = default)
    {
        var existing = await db.PlanEntitlementOverrides
            .FirstOrDefaultAsync(
                o => o.OrganizationId == entry.OrganizationId
                     && o.Key == entry.Key
                     && o.EffectiveFrom == entry.EffectiveFrom,
                cancellationToken);

        if (existing is null)
        {
            db.PlanEntitlementOverrides.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
            return entry;
        }

        existing.ValueType = entry.ValueType;
        existing.ValueDecimal = entry.ValueDecimal;
        existing.ValueBool = entry.ValueBool;
        existing.ValueText = entry.ValueText;
        existing.EffectiveTo = entry.EffectiveTo;
        existing.Reason = entry.Reason;
        existing.CreatedByUserId = entry.CreatedByUserId;
        existing.CreatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }
}
