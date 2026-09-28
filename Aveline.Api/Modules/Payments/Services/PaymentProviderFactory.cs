using Aveline.Api.Modules.Payments.Domain;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// Resolves the adapter registered under a provider key (decision D5, plan §6.7). Keyed DI is used
/// so <see cref="Resolve"/> needs no switch and no name-mapping table: the key <em>is</em> the
/// provider key.
/// </summary>
internal sealed class PaymentProviderFactory(IServiceProvider services, IOptions<PaymentsOptions> options)
    : IPaymentProviderFactory
{
    /// <summary>The mock provider's key, guardrail 2 of plan §7.4.</summary>
    internal const string MockProviderKey = "mock";

    private readonly PaymentsOptions _options = options.Value;

    public IPaymentProvider Active => Resolve(_options.Provider);

    public IPaymentProvider Resolve(string providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new PaymentProviderNotConfiguredException(
                "No payment provider is configured. Set Payments:Provider to 'manual', 'mock', or 'stripe'.");
        }

        // Belt-and-braces on top of the startup Validate: the keyed registration for the mock exists
        // unconditionally, so a config mistake must not be able to activate it by itself.
        if (string.Equals(providerKey, MockProviderKey, StringComparison.OrdinalIgnoreCase)
            && !_options.Mock.Enabled)
        {
            throw new PaymentProviderNotConfiguredException(
                "The mock payment provider is disabled. Set Payments:Mock:Enabled to true to use it.");
        }

        var provider = services.GetKeyedService<IPaymentProvider>(providerKey);

        if (provider is null)
        {
            throw new PaymentProviderNotConfiguredException(
                $"No payment provider is registered for key '{providerKey}'.");
        }

        return provider;
    }
}
