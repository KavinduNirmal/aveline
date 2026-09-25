using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Turns a plan tier into the organisation's subscription row (plan §9.1 F1, gap G8). It is the
/// onboarding-time half of <c>SubscriptionService</c>'s period-aware change path: it owns the
/// "first subscription exists" transition, not plan changes.
/// </summary>
public interface ISubscriptionProvisioner
{
    /// <summary>
    /// Creates or re-prices the single <see cref="OrganizationSubscription"/> for
    /// <paramref name="organizationId"/> at <paramref name="tier"/>. Idempotent: a second call for
    /// the same tier updates the one row rather than adding another.
    /// </summary>
    /// <param name="at">
    /// The instant the price is effective at, or <c>null</c> for now. The billing period is derived
    /// from the same instant.
    /// </param>
    Task<SubscriptionProvisionResult> ProvisionFromTierAsync(
        Guid organizationId,
        PlanTier tier,
        DateTime? at = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What <see cref="ISubscriptionProvisioner"/> wrote, as the onboarding response needs to report it
/// without a second read. <see cref="PriceLkr"/> is <c>null</c> when the price book had no row for
/// the tier and no value was written (P1's rule: a missing price is never coerced to zero).
/// </summary>
public sealed record SubscriptionProvisionResult(
    Guid SubscriptionId,
    PlanTier PlanTier,
    string Status,
    decimal? PriceLkr,
    string Currency);
