using Aveline.Api.Modules.Billing.DTOs;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Applies per-organization entitlement overrides for Enterprise contracts (FR-4.9).
/// </summary>
public interface IEntitlementOverrideService
{
    /// <summary>
    /// Upserts the supplied overrides, writes an audit entry, and returns the
    /// organization's resolved effective entitlements.
    /// </summary>
    Task<IReadOnlyList<EntitlementItemView>> SetOverridesAsync(
        Guid organizationId,
        Guid actorUserId,
        IReadOnlyList<EntitlementOverrideInput> overrides,
        CancellationToken cancellationToken = default);
}
