using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsOptions = Microsoft.Extensions.Options.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// The mock adapter's run through the §11.1 contract suite. Unlike the manual adapter, the mock
/// genuinely satisfies the settled-intent, refund and webhook branches, so this subclass drives the
/// real behaviour rather than the documented refusal.
/// </summary>
/// <remarks>
/// <see cref="TestEndpointScenario.SettledIntent"/> and <see cref="TestEndpointScenario.PartialRefund"/>
/// are arranged by creating the adapter with the <c>tok_aveline_succeed</c> credential and
/// <c>AutoSettle</c> on; <see cref="TestEndpointScenario.Default"/> leaves the credential unset, so
/// the adapter behaves like a hosted-checkout provider awaiting a customer choice.
/// </remarks>
public sealed class MockPaymentProviderContractTests : PaymentProviderContractTests
{
    private const string Secret = "whsec_mock-contract-secret";

    protected override IPaymentProvider CreateProvider(TimeProvider clock, TestEndpointScenario scenario)
    {
        var options = new PaymentsOptions { Currency = "LKR" };
        options.Mock.Enabled = true;
        options.Mock.AutoSettle = true;
        options.Mock.WebhookSigningSecret = Secret;

        var credential = scenario == TestEndpointScenario.Default
            ? null
            : MockPaymentProvider.SucceedToken;

        return new MockPaymentProvider(
            OptionsOptions.Create(options),
            clock,
            NullLogger<MockPaymentProvider>.Instance,
            new PaymentMetrics(),
            credential);
    }

    protected override PaymentWebhookRequest SignedWebhook(string body, DateTimeOffset at) =>
        new(body, MockPaymentProvider.SignedHeaders(Secret, at, body), "203.0.113.7", at);
}
