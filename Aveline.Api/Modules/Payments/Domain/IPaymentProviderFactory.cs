namespace Aveline.Api.Modules.Payments.Domain;

/// <summary>
/// Resolves the payment adapter to use (plan §6.2, decision D5).
/// </summary>
/// <remarks>
/// <see cref="Resolve"/> by stored key is the load-bearing part of the swap story: without it,
/// flipping <c>Payments:Provider</c> from one adapter to another would orphan every open intent and
/// make every in-flight webhook unprocessable.
/// </remarks>
public interface IPaymentProviderFactory
{
    /// <summary>
    /// The provider selected by configuration. Throws
    /// <see cref="PaymentProviderNotConfiguredException"/> when it is unusable.
    /// </summary>
    IPaymentProvider Active { get; }

    /// <summary>
    /// Resolves a provider by the key stored on an intent, so an intent created under one provider
    /// can still be read, refunded, or webhooked after the configuration changes.
    /// </summary>
    /// <exception cref="PaymentProviderNotConfiguredException">The key is blank, unknown, or names
    /// a provider that is disabled in this environment.</exception>
    IPaymentProvider Resolve(string providerKey);
}
