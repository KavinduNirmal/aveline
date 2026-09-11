using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>Read access to plan entitlements and per-organisation overrides.</summary>
public interface IEntitlementRepository
{
    Task<PlanTier> GetOrganizationPlanTierAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Plan rows for a tier effective at <paramref name="at"/>, newest first.</summary>
    Task<IReadOnlyList<PlanEntitlement>> ListEffectivePlanEntitlementsAsync(
        PlanTier tier, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Override rows for an organisation effective at <paramref name="at"/>, newest first.</summary>
    Task<IReadOnlyList<PlanEntitlementOverride>> ListEffectiveOverridesAsync(
        Guid organizationId, DateTime at, CancellationToken cancellationToken = default);
}
