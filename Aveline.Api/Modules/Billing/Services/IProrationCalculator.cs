using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>Which side produced a mid-cycle proration figure (plan §9.3 F3).</summary>
public enum ProrationSource
{
    /// <summary>The active provider computed it: it advertises <c>SupportsProration</c>.</summary>
    Provider,

    /// <summary>Aveline computed it with <see cref="Domain.PlanChangeProration"/>.</summary>
    Local,
}

/// <summary>A proration figure and the path that produced it.</summary>
public sealed record ProrationQuote(decimal AmountLkr, ProrationSource Source);

/// <summary>
/// Prices a mid-cycle plan change (plan §9.3 F3). The provider is asked where it can do the
/// arithmetic, and Aveline falls back to its own documented formula where it cannot.
/// </summary>
public interface IProrationCalculator
{
    /// <summary>
    /// The amount to charge for the rest of the current period: the provider's own calculation when
    /// it advertises <c>SupportsProration</c>, otherwise
    /// <see cref="Domain.PlanChangeProration.Local"/>.
    /// </summary>
    ProrationQuote Compute(ProrationRequest request);

    /// <summary>
    /// The provider's own figure. This is the loud path: an adapter whose capability says it cannot
    /// prorate is refused rather than quietly served Aveline's arithmetic.
    /// </summary>
    /// <exception cref="PaymentProviderNotSupportedException">
    /// The active provider does not advertise <c>SupportsProration</c>, or advertises it without
    /// implementing <see cref="IProrationProvider"/>.
    /// </exception>
    decimal ComputeWithProvider(ProrationRequest request);

    /// <summary>Aveline's documented fallback formula, whatever the provider can do.</summary>
    decimal ComputeLocally(ProrationRequest request);
}
