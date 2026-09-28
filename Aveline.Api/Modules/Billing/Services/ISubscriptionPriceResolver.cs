using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Resolves the list price of a subscription from the commercial price book (FR-1.11, G7/G9).
/// </summary>
public interface ISubscriptionPriceResolver
{
    /// <summary>
    /// The active <c>PlanAllowance</c> price for <paramref name="planTier"/> at
    /// <paramref name="at"/> (<c>null</c> means now), honouring a per-organisation override before
    /// the tier list price and then the global row.
    /// </summary>
    /// <returns>
    /// The price in LKR, or <c>null</c> when no row is effective. A missing price is not zero and
    /// the caller must not coerce it to one.
    /// </returns>
    Task<decimal?> ResolveAsync(
        Guid organizationId,
        PlanTier planTier,
        BillingCycle billingCycle,
        DateTime? at = null,
        CancellationToken cancellationToken = default);
}
