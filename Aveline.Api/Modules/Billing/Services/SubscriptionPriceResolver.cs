using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="ISubscriptionPriceResolver"/>: reads the <c>PlanAllowance</c> rows from the
/// price book and applies <see cref="PriceBookSelection"/>.
/// </summary>
public sealed class SubscriptionPriceResolver(IPricingRepository repository) : ISubscriptionPriceResolver
{
    public async Task<decimal?> ResolveAsync(
        Guid organizationId,
        PlanTier planTier,
        BillingCycle billingCycle,
        DateTime? at = null,
        CancellationToken cancellationToken = default)
    {
        // The price book has no billing-cycle dimension yet and `BillingCycle.Annual` has no
        // behaviour (the plan's Q4 keeps annual billing out of scope), so only a monthly list
        // price can be resolved. Inferring an annual price from a monthly row would under-bill,
        // which is worse than reporting that no annual price exists.
        if (billingCycle != BillingCycle.Monthly)
        {
            return null;
        }

        var asOf = at ?? DateTime.UtcNow;

        // The repository cannot express `OrganizationId IS NULL`, and the whole point of the rule
        // is the precedence between scopes, so the candidate set is read once and the precedence
        // is applied in memory. A price book is a small, slowly-changing table.
        var entries = await repository.ListPriceEntriesAsync(
            BlossomSkuKind.PlanAllowance,
            planTier: null,
            organizationId: null,
            cancellationToken: cancellationToken);

        return PriceBookSelection.Resolve(entries, organizationId, planTier, asOf).PriceLkr;
    }
}
