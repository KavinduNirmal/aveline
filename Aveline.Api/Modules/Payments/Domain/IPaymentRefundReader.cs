namespace Aveline.Api.Modules.Payments.Domain;

/// <summary>
/// An adapter that can read back the refunds it has recorded for a charge (plan §10 Phase 7).
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately an <em>optional</em> capability interface rather than a method on
/// <see cref="IPaymentProvider"/>, for the same reason as <see cref="IProrationProvider"/>: the SPI's
/// member list is the contract every adapter implements (plan §6.2), and an adapter with no refund
/// surface of its own (the manual adapter: refunds there are an operator action in the console) must
/// not be forced to invent one.
/// </para>
/// <para>
/// The reconciliation read uses it when it is available. For an adapter without it the refund half
/// of the comparison is skipped and the report says so in its notes, rather than reporting "no
/// refunds" — which would be an unearned all-clear, the exact outcome the Blossom sibling's shared
/// derivation exists to prevent.
/// </para>
/// </remarks>
public interface IPaymentRefundReader
{
    /// <summary>
    /// Every refund the provider holds against <paramref name="providerIntentId"/>, oldest first.
    /// Returns an empty list when the charge was never refunded; a provider that does not know the
    /// charge is the caller's concern (it reads intent state through
    /// <see cref="IPaymentProvider.GetPaymentIntentAsync"/> first).
    /// </summary>
    Task<IReadOnlyList<ProviderRefund>> ListRefundsAsync(
        string providerIntentId, CancellationToken cancellationToken = default);
}
