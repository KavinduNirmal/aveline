namespace Aveline.Api.Modules.Payments.Domain;

/// <summary>
/// The provider-neutral inputs of a mid-cycle proration (plan §9.3 F3).
/// </summary>
/// <param name="PreviousMonthlyPriceLkr">The monthly list price of the plan being left, in LKR.</param>
/// <param name="NewMonthlyPriceLkr">The monthly list price of the plan being taken, in LKR.</param>
/// <param name="PeriodEnd">The end of the billing period the charge is prorated over.</param>
/// <param name="At">The instant the proration is struck, normally now.</param>
/// <remarks>
/// The type carries prices and dates only: no card, PAN, expiry, or token field exists anywhere in
/// the payment family (constraint C11), and a proration is no exception.
/// </remarks>
public sealed record ProrationRequest(
    decimal PreviousMonthlyPriceLkr,
    decimal NewMonthlyPriceLkr,
    DateOnly PeriodEnd,
    DateOnly At)
{
    /// <summary>
    /// The monthly difference the charge is prorated over. Negative for a downgrade, which is a
    /// change that owes nothing (plan §9.3).
    /// </summary>
    public decimal MonthlyDifferenceLkr => NewMonthlyPriceLkr - PreviousMonthlyPriceLkr;
}

/// <summary>
/// An adapter that can compute a mid-cycle proration itself (plan §9.3, and §8.5's "let the billing
/// provider do this wherever possible").
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately an <em>optional</em> capability interface rather than a method on
/// <see cref="IPaymentProvider"/>: the SPI's member list is the contract every adapter implements
/// (plan §6.2), and an adapter that cannot prorate already says so through
/// <see cref="PaymentProviderCapabilities.SupportsProration"/>. A caller must consult that flag
/// first and only then ask whether the resolved adapter implements this interface.
/// </para>
/// <para>
/// Implementations must not settle anything. The figure they return becomes an ordinary intent
/// amount, so the money rules and the receipt path stay in one place.
/// </para>
/// </remarks>
public interface IProrationProvider
{
    /// <summary>
    /// The provider's own proration for the remainder of the period, in LKR to two decimal places.
    /// </summary>
    decimal ComputeProration(ProrationRequest request);
}
