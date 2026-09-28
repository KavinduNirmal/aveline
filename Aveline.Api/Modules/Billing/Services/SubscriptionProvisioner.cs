using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="ISubscriptionProvisioner"/>. Lives in the Billing module rather than on
/// <c>SubscriptionService</c> because it is the "a tier became the tenant's plan" write, which
/// onboarding owns, not the plan-change lifecycle the subscription service owns; keeping it
/// separate also keeps <c>SubscriptionService</c>'s seven-argument unit-test construction intact.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status.</b> A paid tier starts <see cref="SubscriptionStatus.Trialing"/>: the plan is recorded
/// and the tenant is entitled, while the money is still deferred (plan §14 Q1). An existing row's
/// status is never demoted back to <c>Trialing</c>, so re-selecting the tier on a live
/// (already <c>Active</c>) subscription is a no-op. <c>Seed</c> is a real free plan and is
/// <c>Active</c>, not a trial.
/// </para>
/// <para>
/// <b>Price.</b> The price book is the only source (G9/FR-1.11). A missing row resolves to
/// <c>null</c> and the existing column value is left alone — never coerced to zero — so an unpriced
/// plan writes no <c>Derived</c> charge, while a real zero-priced row is a free plan. That is P1's
/// rule, and it is why this method cannot report a price it did not resolve.
/// </para>
/// <para>
/// <b>Seed.</b> Seed persists an explicit zero-price row rather than skipping the table. The
/// organisation and the subscription cannot then disagree about the plan (G8's "no subscription
/// row" symptom), and the one-row-per-organisation invariant holds for every tier. It is "free",
/// not "paid": the row is <c>Active</c> and carries no provider, no trial and no charge.
/// </para>
/// </remarks>
public sealed class SubscriptionProvisioner(
    AppDbContext db,
    ISubscriptionPriceResolver priceResolver,
    TimeProvider? clock = null) : ISubscriptionProvisioner
{
    private const string Currency = "LKR";

    private TimeProvider Clock => clock ?? TimeProvider.System;

    public async Task<SubscriptionProvisionResult> ProvisionFromTierAsync(
        Guid organizationId,
        PlanTier tier,
        DateTime? at = null,
        CancellationToken cancellationToken = default)
    {
        var now = at ?? Clock.GetUtcNow().UtcDateTime;

        var subscription = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

        if (subscription is null)
        {
            var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            subscription = new OrganizationSubscription
            {
                OrganizationId = organizationId,
                CurrentPeriodStart = periodStart,
                CurrentPeriodEnd = periodStart.AddMonths(1),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.OrganizationSubscriptions.Add(subscription);

            // The free plan is Active, not Trialing. A paid plan is a real agreement whose first
            // collection is deferred, so it opens in the trial state (the state this phase makes
            // reachable for the first time).
            subscription.Status = tier == PlanTier.Seed
                ? SubscriptionStatus.Active
                : SubscriptionStatus.Trialing;
        }

        subscription.PlanTier = tier;
        subscription.SeatsIncluded = SeatsFor(tier);
        subscription.UpdatedAt = now;

        decimal? resolvedPriceLkr = null;
        if (tier == PlanTier.Seed)
        {
            subscription.PriceLkr = 0m;
            resolvedPriceLkr = 0m;
        }
        else if (await priceResolver.ResolveAsync(
                     organizationId, tier, subscription.BillingCycle, at: now, cancellationToken)
                 is { } priceLkr)
        {
            subscription.PriceLkr = priceLkr;
            resolvedPriceLkr = priceLkr;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new SubscriptionProvisionResult(
            subscription.Id,
            subscription.PlanTier,
            subscription.Status.ToString(),
            resolvedPriceLkr,
            Currency);
    }

    private static int SeatsFor(PlanTier tier) =>
        (int)(PlanEntitlementDefaults.For(tier).TryGetValue("staff.max", out var staff)
            ? staff.Number ?? 1m
            : 1m);
}
