using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="IProrationCalculator"/>. It reads the capability off the adapter the payment
/// module selected, because that flag is the only thing that says whether the provider can do the
/// arithmetic (plan §6.2: "callers must consult this rather than assume").
/// </summary>
public sealed class ProrationCalculator(IPaymentProviderFactory providers) : IProrationCalculator
{
    public ProrationQuote Compute(ProrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return providers.Active.Capabilities.SupportsProration
            ? new ProrationQuote(ComputeWithProvider(request), ProrationSource.Provider)
            : new ProrationQuote(ComputeLocally(request), ProrationSource.Local);
    }

    public decimal ComputeWithProvider(ProrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = providers.Active;

        if (!provider.Capabilities.SupportsProration)
        {
            throw new PaymentProviderNotSupportedException(
                $"The configured payment provider '{provider.Key}' does not support proration "
                + "(Payments:Provider); Aveline must use its local fallback instead of asking it to "
                + "compute a mid-cycle plan-change charge.");
        }

        if (provider is not IProrationProvider proration)
        {
            // Advertising a capability without implementing it is an adapter contract hole, and it
            // is reported rather than papered over with the local formula.
            throw new PaymentProviderNotSupportedException(
                $"The configured payment provider '{provider.Key}' advertises SupportsProration but "
                + $"does not implement {nameof(IProrationProvider)}.");
        }

        return proration.ComputeProration(request);
    }

    public decimal ComputeLocally(ProrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PlanChangeProration.Local(
            request.PreviousMonthlyPriceLkr,
            request.NewMonthlyPriceLkr,
            request.PeriodEnd,
            request.At);
    }
}
