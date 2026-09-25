using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsOptions = Microsoft.Extensions.Options.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// The adapter-specific arrangement one contract case asks for. The mock adapter (P2-B) uses it to
/// select a scenario token; an adapter with no provider-side state (the manual adapter) ignores it.
/// </summary>
/// <param name="Name">A stable scenario name.</param>
public sealed record TestEndpointScenario(string Name)
{
    public static readonly TestEndpointScenario Default = new("default");

    /// <summary>An arrangement under which a create settles synchronously, when the adapter can.</summary>
    public static readonly TestEndpointScenario SettledIntent = new("settled-intent");

    /// <summary>An arrangement under which a partial refund is meaningful, when the adapter can.</summary>
    public static readonly TestEndpointScenario PartialRefund = new("partial-refund");

    public override string ToString() => Name;
}

/// <summary>
/// The contract every <see cref="IPaymentProvider"/> adapter must satisfy (plan §11.1). This suite
/// is the enforceable definition of "implementing the interface is enough": any adapter that passes
/// it may be configured in any environment the startup guard permits.
/// </summary>
/// <remarks>
/// <para>
/// The suite is capability-driven rather than assertion-free. Where a declared capability excludes an
/// operation, the member asserts the adapter's documented refusal
/// (<see cref="PaymentProviderNotSupportedException"/>) instead of inventing a state change. That is
/// what makes <c>UnsupportedCall_ThrowsNotSupported</c> and <c>Capabilities_AreTruthful</c> the two
/// load-bearing members: an adapter cannot advertise a capability it does not have, and it cannot
/// silently succeed at something it cannot do.
/// </para>
/// <para>
/// One concrete subclass exists per adapter. P2-A ships the manual adapter; P2-B adds the mock.
/// </para>
/// </remarks>
public abstract class PaymentProviderContractTests
{
    /// <summary>Builds the adapter under test.</summary>
    /// <param name="clock">The clock the adapter must read (the repository already registers a
    /// <see cref="TimeProvider"/>).</param>
    /// <param name="scenario">The arrangement this case needs; see <see cref="TestEndpointScenario"/>.</param>
    protected abstract IPaymentProvider CreateProvider(TimeProvider clock, TestEndpointScenario scenario);

    /// <summary>
    /// Whether the adapter can verify provider webhooks. <see cref="PaymentProviderCapabilities"/>
    /// has no webhook flag, so the suite asks the subclass directly; the manual adapter has no
    /// webhook and says so, and the webhook members then assert the honest refusal.
    /// </summary>
    protected virtual bool SupportsWebhooks => true;

    /// <summary>Whether a create can produce a <c>Succeeded</c> intent. The manual adapter cannot:
    /// settlement is an operator action outside the SPI.</summary>
    protected virtual bool CanProduceSettledIntent => true;

    /// <summary>
    /// Whether a settled outcome is only observable through a follow-up read. True for a
    /// hosted-checkout adapter: its create always returns the customer handoff
    /// (<c>RequiresAction</c>) and the charge settles later, so the "settled intent" the suite needs
    /// is the state of a read, not the state of the create. False for the mock, which can settle in
    /// place at creation.
    /// </summary>
    protected virtual bool SettlesOnlyOnRead => false;

    // ---------------------------------------------------------------- helpers

    protected static string NewKey() => $"contract-{Guid.NewGuid():N}";

    protected static CreateProviderIntentRequest CreateRequest(
        string idempotencyKey, Money? amount = null, Guid? intentId = null) => new(
        IntentId: intentId ?? Guid.CreateVersion7(),
        Amount: amount ?? Money.Lkr(3500m),
        Purpose: PaymentPurpose.BlossomTopUp,
        Description: "Contract test intent.",
        CustomerReference: "contract-org",
        IdempotencyKey: idempotencyKey,
        ReturnUrl: new Uri("https://example.test/payments/return"),
        ExpiresAt: null);

    protected static ProviderRefundRequest RefundRequest(string providerIntentId, Money amount) =>
        new(providerIntentId, amount, NewKey(), "Contract test refund.");

    protected static CreateProviderSubscriptionRequest SubscriptionRequest() => new(
        ProviderSubscriptionId: "contract-subscription",
        ProviderCustomerId: "contract-customer",
        RecurringAmount: Money.Lkr(3500m),
        Interval: "month",
        IdempotencyKey: NewKey(),
        AllowedPaymentMethodTypes: null);

    /// <summary>A correctly signed webhook for the fixed contract body, using the adapter's own
    /// scheme. An adapter that declares <see cref="SupportsWebhooks"/> must override this.</summary>
    protected virtual PaymentWebhookRequest SignedWebhook(string body, DateTimeOffset at) =>
        throw new InvalidOperationException(
            "An adapter that supports webhooks must supply a signed request for the contract suite.");

    private static PaymentWebhookRequest UnsignedWebhook(string body, DateTimeOffset at) =>
        new(body, new Dictionary<string, string>(), "203.0.113.7", at);

    private static async Task<PaymentProviderNotSupportedException> NotSupportedAsync(Func<Task> call) =>
        await Assert.ThrowsAsync<PaymentProviderNotSupportedException>(call);

    /// <summary>Creates an intent and, for a capable adapter, settles it; otherwise returns the
    /// non-settled intent as-is.</summary>
    private async Task<ProviderPaymentIntent> CreateSettledAsync(IPaymentProvider provider)
    {
        var created = await provider.CreatePaymentIntentAsync(CreateRequest(NewKey()));

        if (!SettlesOnlyOnRead)
        {
            return created;
        }

        // The hosted-checkout shape: the create returns the handoff, and the provider's settled state
        // is what a read reports once the customer has paid. This is the same read the endpoint layer
        // polls, so the suite is still asserting the adapter's real behaviour.
        var settled = await provider.GetPaymentIntentAsync(created.ProviderIntentId);

        return settled ?? created;
    }

    // ------------------------------------------------------------ the contract

    [Fact]
    public async Task Create_IsIdempotentOnTheRequestKey()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);
        var request = CreateRequest(NewKey(), Money.Lkr(3500m));

        var first = await provider.CreatePaymentIntentAsync(request);
        // The same idempotency key with a different amount must return the original intent, not
        // create a second charge.
        var replay = await provider.CreatePaymentIntentAsync(request with { Amount = Money.Lkr(9000m) });

        replay.ProviderIntentId.Should().Be(first.ProviderIntentId);
        replay.Amount.AmountMinor.Should().Be(first.Amount.AmountMinor);
    }

    [Fact]
    public async Task Create_RejectsNonPositiveAmount()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);

        await NotSupportedAsync(() =>
            provider.CreatePaymentIntentAsync(CreateRequest(NewKey(), new Money(0, "LKR"))));
        await NotSupportedAsync(() =>
            provider.CreatePaymentIntentAsync(CreateRequest(NewKey(), new Money(-1, "LKR"))));
    }

    [Fact]
    public async Task Get_UnknownIntentId_ReturnsNull()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);

        var found = await provider.GetPaymentIntentAsync($"contract-missing-{Guid.NewGuid():N}");

        found.Should().BeNull();
    }

    [Fact]
    public async Task Cancel_SettledIntent_ThrowsStateOrNotSupported()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.SettledIntent);

        if (!CanProduceSettledIntent)
        {
            // A settled intent cannot exist, and the adapter must refuse rather than pretend.
            await NotSupportedAsync(() =>
                provider.CancelPaymentIntentAsync("contract-unknown", "Contract test cancellation."));
            return;
        }

        var created = await CreateSettledAsync(provider);
        created.Status.Should().Be(PaymentProviderStatus.Succeeded);

        var exception = await Record.ExceptionAsync(() =>
            provider.CancelPaymentIntentAsync(created.ProviderIntentId, "Contract test cancellation."));

        exception.Should().BeAssignableTo<PaymentDomainException>();
        (exception is PaymentIntentStateException || exception is PaymentProviderNotSupportedException)
            .Should().BeTrue("a settled intent must fail with 409 or 501, never succeed");
    }

    [Fact]
    public async Task Refund_MoreThanSettled_ThrowsStateOrNotSupported()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.SettledIntent);

        if (!CanProduceSettledIntent)
        {
            await NotSupportedAsync(() => provider.RefundAsync(RefundRequest("contract-unknown", Money.Lkr(1))));
            return;
        }

        var created = await CreateSettledAsync(provider);
        created.Amount.AmountMinor.Should().BeGreaterThan(0);

        var exception = await Record.ExceptionAsync(() => provider.RefundAsync(
            RefundRequest(created.ProviderIntentId, new Money(created.Amount.AmountMinor + 1, "LKR"))));

        exception.Should().BeAssignableTo<PaymentDomainException>();
        (exception is PaymentIntentStateException || exception is PaymentProviderNotSupportedException)
            .Should().BeTrue("a refund beyond the settled amount must fail with 409 or 501");
    }

    [Fact]
    public async Task Refund_BeyondCapabilities_ThrowsNotSupported()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.PartialRefund);

        if (!provider.Capabilities.SupportsPartialRefunds)
        {
            await NotSupportedAsync(() => provider.RefundAsync(RefundRequest("contract-unknown", Money.Lkr(1))));
            return;
        }

        var created = await CreateSettledAsync(provider);
        var partial = await provider.RefundAsync(
            RefundRequest(created.ProviderIntentId, new Money(100, "LKR")));

        partial.Amount.AmountMinor.Should().Be(100);
        partial.ProviderIntentId.Should().Be(created.ProviderIntentId);
    }

    [Fact]
    public async Task VerifyWebhook_RejectsBadSignature()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);

        if (!SupportsWebhooks)
        {
            await NotSupportedAsync(() =>
            {
                provider.VerifyAndParseWebhook(UnsignedWebhook("{}", DateTimeOffset.UtcNow));
                return Task.CompletedTask;
            });
            return;
        }

        var request = UnsignedWebhook("{\"id\":\"evt_contract\"}", DateTimeOffset.UtcNow);

        var exception = Record.Exception(() => provider.VerifyAndParseWebhook(request));

        exception.Should().BeOfType<PaymentWebhookVerificationException>();
    }

    [Fact]
    public async Task VerifyWebhook_RejectsStaleTimestamp()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);

        if (!SupportsWebhooks)
        {
            await NotSupportedAsync(() =>
            {
                provider.VerifyAndParseWebhook(UnsignedWebhook("{}", DateTimeOffset.UtcNow));
                return Task.CompletedTask;
            });
            return;
        }

        // Ten minutes old: outside the default five-minute tolerance.
        var stale = SignedWebhook("{\"id\":\"evt_stale\"}", DateTimeOffset.UtcNow.AddMinutes(-10));

        var exception = Record.Exception(() => provider.VerifyAndParseWebhook(stale));

        exception.Should().BeOfType<PaymentWebhookVerificationException>();
    }

    [Fact]
    public async Task VerifyWebhook_IsStableForAFixedBodyAndSecret()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);

        if (!SupportsWebhooks)
        {
            await NotSupportedAsync(() =>
            {
                provider.VerifyAndParseWebhook(UnsignedWebhook("{}", DateTimeOffset.UtcNow));
                return Task.CompletedTask;
            });
            return;
        }

        var at = DateTimeOffset.UtcNow;
        const string body = "{\"id\":\"evt_fixed\",\"type\":\"intent.succeeded\"}";

        var first = provider.VerifyAndParseWebhook(SignedWebhook(body, at));
        var second = provider.VerifyAndParseWebhook(SignedWebhook(body, at));

        second.ProviderEventId.Should().Be(first.ProviderEventId);
        second.Type.Should().Be(first.Type);
    }

    [Fact]
    public async Task UnsupportedCall_ThrowsNotSupported_RatherThanSilentlySucceeding()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);
        var capabilities = provider.Capabilities;
        var checkedAny = false;

        if (!capabilities.SupportsRecurringSubscriptions)
        {
            checkedAny = true;
            await NotSupportedAsync(() => provider.CreateOrUpdateSubscriptionAsync(SubscriptionRequest()));
        }

        if (!capabilities.SupportsCancelAtPeriodEnd)
        {
            checkedAny = true;
            await NotSupportedAsync(() =>
                provider.CancelSubscriptionAsync("contract-subscription", atPeriodEnd: true));
        }

        if (!capabilities.SupportsPartialRefunds)
        {
            checkedAny = true;
            await NotSupportedAsync(() => provider.RefundAsync(RefundRequest("contract-unknown", Money.Lkr(1))));
        }

        if (!SupportsWebhooks)
        {
            checkedAny = true;
            await NotSupportedAsync(() =>
            {
                provider.VerifyAndParseWebhook(UnsignedWebhook("{}", DateTimeOffset.UtcNow));
                return Task.CompletedTask;
            });
        }

        if (!checkedAny)
        {
            // No declared limitation to cross-check: prove the adapter still refuses an operation it
            // cannot honour rather than fabricating a result, by refunding an intent it never made.
            await Assert.ThrowsAnyAsync<PaymentDomainException>(() =>
                provider.RefundAsync(RefundRequest("contract-unknown-intent", Money.Lkr(1))));
        }
    }

    [Fact]
    public async Task Capabilities_AreTruthful_ForEveryCallTheSuiteMakes()
    {
        var provider = CreateProvider(TimeProvider.System, TestEndpointScenario.Default);
        var capabilities = provider.Capabilities;

        var created = await provider.CreatePaymentIntentAsync(CreateRequest(NewKey()));

        created.Amount.AmountMinor.Should().Be(350000);
        created.Amount.Currency.Should().Be("LKR");

        if (capabilities.SupportsHostedCheckout)
        {
            created.CheckoutUrl.Should().NotBeNull("a provider that advertises hosted checkout must return a URL");
        }
        else
        {
            created.CheckoutUrl.Should().BeNull("a provider without hosted checkout must not invent a URL");
        }

        if (capabilities.SettlesAsynchronously)
        {
            created.Status.Should().BeOneOf(
                new[] { PaymentProviderStatus.RequiresAction, PaymentProviderStatus.Processing },
                "an asynchronous provider cannot report a terminal status at creation");
        }

        if (!capabilities.SupportsRecurringSubscriptions)
        {
            await NotSupportedAsync(() => provider.CreateOrUpdateSubscriptionAsync(SubscriptionRequest()));
        }

        if (!capabilities.SupportsCancelAtPeriodEnd)
        {
            await NotSupportedAsync(() =>
                provider.CancelSubscriptionAsync("contract-subscription", atPeriodEnd: true));
        }

        if (!capabilities.SupportsPartialRefunds)
        {
            await NotSupportedAsync(() => provider.RefundAsync(RefundRequest("contract-unknown", Money.Lkr(1))));
        }
    }
}

/// <summary>
/// The manual adapter's contract run. The manual adapter is the honest representation of the system
/// today (an operator confirms receipt), so it declares no hosted checkout, no recurring billing, no
/// refunds and no webhook, and refuses every call it cannot honour.
/// </summary>
public sealed class ManualPaymentProviderContractTests : PaymentProviderContractTests
{
    protected override IPaymentProvider CreateProvider(TimeProvider clock, TestEndpointScenario scenario) =>
        new ManualPaymentProvider();

    protected override bool SupportsWebhooks => false;

    protected override bool CanProduceSettledIntent => false;
}

/// <summary>
/// The OnePay adapter's run through the §11.1 contract suite (plan Phase 8). OnePay settles LKR and
/// exposes a hosted redirection checkout, so the create/read/refund branches are driven against a
/// scripted HTTP layer rather than a declared refusal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a scripted transport.</b> There is no sandbox account in this environment, so the
/// provider's HTTP surface is supplied by <see cref="OnePayHttpStub"/>. The stub answers with the
/// documented response shapes recorded in
/// <c>Modules/Payments/Providers/OnePayPaymentProvider.cs</c>; it is a transport double, not a
/// behavioural one, so the adapter still decides every status.
/// </para>
/// <para>
/// <b>Declared limitations.</b> OnePay signs no webhook, so <see cref="SupportsWebhooks"/> is false
/// and the suite asserts the adapter's honest refusal; that is the same branch the manual adapter
/// takes, and the reason is different (see the adapter's remarks).
/// </para>
/// </remarks>
public sealed class OnePayPaymentProviderContractTests : PaymentProviderContractTests
{
    protected override IPaymentProvider CreateProvider(TimeProvider clock, TestEndpointScenario scenario) =>
        OnePayTestAdapter.Build(clock, OnePayTestAdapter.Handlers(scenario));

    protected override bool SupportsWebhooks => false;

    /// <summary>OnePay is a hosted-checkout provider: the create returns the handoff and the settled
    /// state comes from the status read the endpoint layer polls.</summary>
    protected override bool SettlesOnlyOnRead => true;

}

/// <summary>
/// The shared arrangement for the OnePay adapter's test runs: the options, the stub transport
/// factory, and the scripted provider responses. Hand-written, in the repository's style; no mocking
/// framework is involved.
/// </summary>
internal static class OnePayTestAdapter
{
    internal const string AppId = "80NR1189D04CD635D8ACD";
    internal const string HashSalt = "test-hash-salt";
    internal const string AppToken = "test-app-token";

    /// <summary>The provider's documented success status code (callback sample, api-implementation).</summary>
    internal const string SucceededStatus = "1";

    internal static PaymentsOptions Options(string provider = "manual")
    {
        var options = new PaymentsOptions { Provider = provider, Currency = "LKR" };
        options.OnePay.AppId = AppId;
        options.OnePay.HashSalt = HashSalt;
        options.OnePay.AppToken = AppToken;
        options.OnePay.CustomerDefaults.FirstName = "Nimal";
        options.OnePay.CustomerDefaults.LastName = "Perera";
        options.OnePay.CustomerDefaults.PhoneNumber = "+94771234567";
        options.OnePay.CustomerDefaults.Email = "customer@example.test";
        return options;
    }

    internal static OnePayPaymentProvider Build(TimeProvider clock, OnePayHttpStub stub) =>
        Build(clock, stub, Options());

    internal static OnePayPaymentProvider Build(
        TimeProvider clock, OnePayHttpStub stub, PaymentsOptions options) =>
        new(
            new StubHttpClientFactory(stub),
            OptionsOptions.Create(options),
            clock,
            NullLogger<OnePayPaymentProvider>.Instance,
            new PaymentMetrics());

    /// <summary>
    /// The provider responses the contract suite needs: creation into the hosted checkout, and a
    /// settled read for <see cref="TestEndpointScenario.SettledIntent"/> and
    /// <see cref="TestEndpointScenario.PartialRefund"/>.
    /// </summary>
    internal static OnePayHttpStub Handlers(TestEndpointScenario scenario)
    {
        var stub = new OnePayHttpStub();

        // A create always hands the customer to OnePay's hosted page; the transaction id is what the
        // status read is keyed on. Matched on method as well as path, because OnePay's create and
        // status endpoints share the word "transaction".
        stub.WhenPathContains(
            "checkout",
            request => request.Is("POST")
                ? OnePayHttpStub.Json(
                    """
                    {
                      "status": 1,
                      "data": {
                        "transaction_id": "ONP2026010100001",
                        "redirect_url": "https://sandbox.onepay.lk/pay/ONP2026010100001"
                      }
                    }
                    """)
                : null);

        // The read: settled once a scenario asks for it, still awaiting the customer otherwise. A read
        // for a transaction this stub never handed out is the provider's 404, which is how the suite's
        // unknown-id member gets a real answer instead of a fabricated one.
        stub.When(
            request => request.Path.Contains("transaction/status", StringComparison.OrdinalIgnoreCase),
            request => stub.IsKnownTransaction(request)
                ? OnePayHttpStub.Json(
                    scenario == TestEndpointScenario.Default
                        ? """
                          {
                            "status": 1,
                            "data": {
                              "transaction_id": "ONP2026010100001",
                              "status": "PENDING",
                              "status_message": "PENDING",
                              "amount": "3500.00",
                              "currency": "LKR"
                            }
                          }
                          """
                        : $$"""
                          {
                            "status": 1,
                            "data": {
                              "transaction_id": "ONP2026010100001",
                              "status": "{{SucceededStatus}}",
                              "status_message": "SUCCESS",
                              "amount": "3500.00",
                              "currency": "LKR"
                            }
                          }
                          """)
                : OnePayHttpStub.Status(System.Net.HttpStatusCode.NotFound));

        // A refund answers in the documented `refund-initiated` shape.
        stub.WhenPathContains(
            "refund",
            request => request.Is("POST")
                ? OnePayHttpStub.Json(
                    """
                    {
                      "status": 1,
                      "data": {
                        "refund_id": "ONPRF2026010100001",
                        "status": "refund-initiated",
                        "amount": "100.00",
                        "currency": "LKR"
                      }
                    }
                    """)
                : null);

        return stub;
    }

    private sealed class StubHttpClientFactory(OnePayHttpStub stub) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(stub) { BaseAddress = OnePayHttpStub.BaseAddress };
    }
}
