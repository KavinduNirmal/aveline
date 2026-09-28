using System.Diagnostics.Metrics;
using System.Reflection;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OptionsOptions = Microsoft.Extensions.Options.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// The mock adapter's own behaviour (plan §7, TDD seed §7.5). These are provider-level cases: every
/// row of the §7.5 matrix that needs neither HTTP nor the settlement services. The rows that need a
/// route, a database row, a Blossom grant or an income entry (M4's "no row", M7 to M11, M13 to M15,
/// M17, M18) belong to P2-B2's endpoint and service suites; the provider-level halves that can be
/// asserted here are asserted here.
/// </summary>
/// <remarks>
/// Every test credential is an <em>input string</em> passed to a method. No credential is ever a
/// property of a persisted model or a wire DTO (constraint C11, asserted by M19 below).
/// </remarks>
public class MockPaymentProviderTests
{
    private const string Secret = "whsec_mock-unit-test-secret";

    private static readonly string[] Section12Instruments =
    [
        "aveline.payment.intent.created",
        "aveline.payment.settlement",
        "aveline.payment.settlement_latency_ms",
        "aveline.payment.webhook.received",
        "aveline.payment.webhook.verification_failures",
        "aveline.payment.webhook.unprocessed_backlog",
        "aveline.payment.refund",
        "aveline.payment.provider.errors",
        "aveline.payment.mock_provider_active",
        "aveline.payment.unreconciled_intents",
    ];

    // ------------------------------------------------------------ data sources

    /// <summary>Every one of the eleven Stripe-compatible card rows of §7.2.</summary>
    public static IEnumerable<object[]> EveryCardNumber =>
        MockPaymentProvider.CardNumbers.Select(card => new object[] { card });

    /// <summary>Every accepted <c>pm_card_*</c>/<c>tok_*</c> synonym of §7.2.</summary>
    public static IEnumerable<object[]> EveryCredentialSynonym =>
        MockPaymentProvider.CredentialSynonyms.Select(credential => new object[] { credential });

    /// <summary>Every one of the twelve Aveline-specific scenario tokens of §7.2.</summary>
    public static IEnumerable<object[]> EveryScenarioToken =>
        MockPaymentProvider.ScenarioTokens.Select(token => new object[] { token });

    // ------------------------------------------------------------ arrangement

    private static PaymentsOptions Configured(Action<MockProviderOptions>? configure = null)
    {
        var options = new PaymentsOptions { Currency = "LKR" };
        options.Mock.Enabled = true;
        options.Mock.AutoSettle = true;
        options.Mock.WebhookSigningSecret = Secret;
        configure?.Invoke(options.Mock);
        return options;
    }

    private static MockPaymentProvider Create(
        TimeProvider? clock = null,
        PaymentsOptions? options = null,
        string? credential = null,
        CapturingLogger<MockPaymentProvider>? logger = null,
        PaymentMetrics? metrics = null) =>
        new(
            OptionsOptions.Create(options ?? Configured()),
            clock ?? TimeProvider.System,
            logger ?? new CapturingLogger<MockPaymentProvider>(),
            metrics ?? new PaymentMetrics(),
            credential);

    private static CreateProviderIntentRequest Request(
        string? idempotencyKey = null, Money? amount = null, Guid? intentId = null) => new(
        IntentId: intentId ?? Guid.CreateVersion7(),
        Amount: amount ?? Money.Lkr(3500m),
        Purpose: PaymentPurpose.BlossomTopUp,
        Description: "Mock provider test intent.",
        CustomerReference: "org-mock-tests",
        IdempotencyKey: idempotencyKey ?? $"mock-{Guid.NewGuid():N}",
        ReturnUrl: null,
        ExpiresAt: null);

    private static ProviderRefundRequest RefundRequest(string providerIntentId, Money amount) =>
        new(providerIntentId, amount, $"refund-{Guid.NewGuid():N}", "Mock refund test.");

    // ============================================================ §7.2 credentials

    [Theory]
    [MemberData(nameof(EveryCardNumber))]
    public async Task CardNumber_CreateOutcome_MatchesTheStripeTable(string cardNumber)
    {
        // M2/M3 generalised: every documented card row produces its documented outcome.
        using var metrics = new PaymentMetrics();
        var provider = Create(metrics: metrics);
        var request = Request();

        var intent = await provider.CreatePaymentIntentAsync(request, cardNumber);

        var row = MockPaymentProvider.ResolveCredential(cardNumber);
        row.Should().NotBeNull();
        intent.Status.Should().Be(row!.Status);
        intent.FailureCode.Should().Be(row.FailureCode);
        intent.FailureMessage.Should().Be(row.FailureMessage);

        if (row.Status == PaymentProviderStatus.Succeeded)
        {
            intent.FailureCode.Should().BeNull();
        }
    }

    [Theory]
    [MemberData(nameof(EveryCredentialSynonym))]
    public async Task CredentialSynonym_ResolvesToItsCanonicalCardRow(string synonym)
    {
        using var metrics = new PaymentMetrics();
        var provider = Create(metrics: metrics);

        var intent = await provider.CreatePaymentIntentAsync(Request(), synonym);

        var resolved = MockPaymentProvider.ResolveCredential(synonym);
        resolved.Should().NotBeNull();
        resolved!.CanonicalCredential.Should().NotBeNullOrEmpty();

        var canonical = MockPaymentProvider.ResolveCredential(resolved.CanonicalCredential);
        canonical.Should().NotBeNull();
        intent.Status.Should().Be(canonical!.Status);
        intent.FailureCode.Should().Be(canonical.FailureCode);
        intent.FailureMessage.Should().Be(canonical.FailureMessage);
    }

    [Theory]
    [MemberData(nameof(EveryScenarioToken))]
    public async Task ScenarioToken_ProducesItsDocumentedCreateOutcome(string token)
    {
        using var metrics = new PaymentMetrics();
        var provider = Create(metrics: metrics);

        if (token == MockPaymentProvider.TimeoutToken)
        {
            var timedOut = () => provider.CreatePaymentIntentAsync(Request(), token);

            await timedOut.Should().ThrowAsync<PaymentProviderTransportException>();
            return;
        }

        var intent = await provider.CreatePaymentIntentAsync(Request(), token);

        var row = MockPaymentProvider.ResolveCredential(token);
        row.Should().NotBeNull();
        intent.Status.Should().Be(row!.Status);
    }

    [Fact]
    public void TheClosedCredentialTable_HasElevenCardsAndTwelveScenarioTokens()
    {
        MockPaymentProvider.CardNumbers.Should().HaveCount(11);
        MockPaymentProvider.ScenarioTokens.Should().HaveCount(12);
        MockPaymentProvider.CredentialSynonyms.Should().NotBeEmpty();

        foreach (var credential in MockPaymentProvider.CardNumbers
                     .Concat(MockPaymentProvider.ScenarioTokens)
                     .Concat(MockPaymentProvider.CredentialSynonyms))
        {
            MockPaymentProvider.ResolveCredential(credential).Should().NotBeNull(
                $"'{credential}' is part of the closed table and must resolve");
        }
    }

    [Theory]
    [InlineData("pm_card_not_real")]
    [InlineData("tok_not_real")]
    [InlineData("4111111111111111")]
    [InlineData("not-a-credential")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UnrecognisedCredential_IsRejectedWith400UnknownTestCredential(string credential)
    {
        var provider = Create();

        var create = () => provider.CreatePaymentIntentAsync(Request(), credential);

        var exception = await create.Should().ThrowAsync<UnknownTestCredentialException>();
        exception.Which.StatusCode.Should().Be(400);
        exception.Which.ErrorCode.Should().Be("unknown-test-credential");
    }

    [Fact]
    public async Task UnrecognisedCredential_OnApply_IsRejectedToo()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.RequiresActionToken);

        var apply = () => provider.ApplyTestCredentialAsync(created.ProviderIntentId, "tok_not_real");

        await apply.Should().ThrowAsync<UnknownTestCredentialException>();
    }

    // ============================================================ §7.5 M1, M2, M3

    [Fact]
    public async Task M1_SucceedToken_SettlesInPlace_WhenAutoSettleIsTrue()
    {
        var provider = Create();

        var intent = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.SucceedToken);

        intent.Status.Should().Be(PaymentProviderStatus.Succeeded);
        intent.Amount.AmountMinor.Should().Be(350000);
        intent.Amount.Currency.Should().Be("LKR");
        // Settles in place, so there is no page to send the customer to.
        intent.CheckoutUrl.Should().BeNull();
        intent.FailureCode.Should().BeNull();
    }

    [Fact]
    public async Task M2_StripeCompatibleCard_SettlesInPlace()
    {
        var provider = Create();

        var intent = await provider.CreatePaymentIntentAsync(Request(), "4242424242424242");

        intent.Status.Should().Be(PaymentProviderStatus.Succeeded);
        intent.CheckoutUrl.Should().BeNull();
    }

    [Fact]
    public async Task M3_DeclinedCard_FailsWithInsufficientFunds()
    {
        var provider = Create();

        var intent = await provider.CreatePaymentIntentAsync(Request(), "4000000000009995");

        intent.Status.Should().Be(PaymentProviderStatus.Failed);
        intent.FailureCode.Should().Be("card_declined");
        intent.FailureMessage.Should().Be("insufficient_funds");
        // No settled money, so the intent is never given a checkout URL it cannot use.
        intent.CheckoutUrl.Should().BeNull();
    }

    [Fact]
    public async Task AttachThenDeclineCard_SucceedsOnCreate_AndDeclinesOnSettle()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), "4000000000000341");

        created.Status.Should().Be(PaymentProviderStatus.Succeeded);

        var settled = await provider.ApplyTestCredentialAsync(
            created.ProviderIntentId, "4000000000000341");

        settled.Status.Should().Be(PaymentProviderStatus.Failed);
        settled.FailureCode.Should().Be("card_declined");
        settled.FailureMessage.Should().Be("generic_decline");
    }

    [Fact]
    public async Task SucceedToken_WithoutAutoSettle_WaitsForTheSettlePath()
    {
        var options = Configured(mock => mock.AutoSettle = false);
        var provider = Create(options: options);

        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.SucceedToken);

        created.Status.Should().Be(PaymentProviderStatus.RequiresAction);
        created.CheckoutUrl.Should().NotBeNull();

        var settled = await provider.ApplyTestCredentialAsync(
            created.ProviderIntentId, MockPaymentProvider.SucceedToken);

        settled.Status.Should().Be(PaymentProviderStatus.Succeeded);
    }

    [Fact]
    public async Task ForceFailureCode_FailsEveryCreatedIntent()
    {
        var options = Configured(mock => mock.ForceFailureCode = "forced_decline");
        var provider = Create(options: options);

        var intent = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.SucceedToken);

        intent.Status.Should().Be(PaymentProviderStatus.Failed);
        intent.FailureCode.Should().Be("forced_decline");
    }

    // ============================================================ §7.5 M5, M6

    [Fact]
    public async Task M5_PendingToken_IsProcessing_WithACheckoutUrl()
    {
        var provider = Create();

        var intent = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.PendingToken);

        intent.Status.Should().Be(PaymentProviderStatus.Processing);
        intent.CheckoutUrl.Should().NotBeNull();
        intent.FailureCode.Should().BeNull();
    }

    [Fact]
    public async Task M6_RequiresActionToken_PollingIsStable()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(
            Request(), MockPaymentProvider.RequiresActionToken);

        created.Status.Should().Be(PaymentProviderStatus.RequiresAction);

        var firstPoll = await provider.GetPaymentIntentAsync(created.ProviderIntentId);
        var secondPoll = await provider.GetPaymentIntentAsync(created.ProviderIntentId);

        firstPoll.Should().Be(created);
        secondPoll.Should().Be(created);
        secondPoll!.Status.Should().Be(PaymentProviderStatus.RequiresAction);
    }

    [Fact]
    public async Task PendingToken_CanBeSettledThroughTheApplyPath()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.PendingToken);

        var settled = await provider.ApplyTestCredentialAsync(
            created.ProviderIntentId, MockPaymentProvider.SucceedToken);

        settled.Status.Should().Be(PaymentProviderStatus.Succeeded);
        settled.ProviderIntentId.Should().Be(created.ProviderIntentId);
        settled.Amount.Should().Be(created.Amount);
    }

    [Fact]
    public async Task ExpireToken_RequiresAction_AndExpiresInSixtySeconds()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var provider = Create(clock: new FixedTimeProvider(now));

        var intent = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.ExpireToken);

        intent.Status.Should().Be(PaymentProviderStatus.RequiresAction);
        intent.CheckoutUrl.Should().NotBeNull();
        intent.ExpiresAt.Should().Be(now.UtcDateTime.AddSeconds(60));
    }

    [Fact]
    public async Task TimeoutToken_RaisesTransportException()
    {
        var provider = Create();

        var create = () => provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.TimeoutToken);

        await create.Should().ThrowAsync<PaymentProviderTransportException>();
    }

    // ============================================================ §7.5 M12

    [Fact]
    public async Task M12_SameIdempotencyKey_ReturnsTheOriginalIntent()
    {
        var provider = Create();
        var key = $"idempotent-{Guid.NewGuid():N}";
        var request = Request(key);

        var first = await provider.CreatePaymentIntentAsync(request, MockPaymentProvider.SucceedToken);
        var replay = await provider.CreatePaymentIntentAsync(
            request with { Amount = Money.Lkr(9999m) }, MockPaymentProvider.SucceedToken);

        replay.ProviderIntentId.Should().Be(first.ProviderIntentId);
        replay.Should().Be(first);
        (await provider.GetPaymentIntentAsync(first.ProviderIntentId)).Should().Be(first);
    }

    // ============================================================ §7.5 M16

    [Fact]
    public async Task M16_RefundFailToken_RaisesProviderError_AndLeavesTheIntentUnchanged()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.RefundFailToken);
        created.Status.Should().Be(PaymentProviderStatus.Succeeded);

        var refund = () => provider.RefundAsync(RefundRequest(created.ProviderIntentId, Money.Lkr(1m)));

        await refund.Should().ThrowAsync<PaymentProviderTransportException>();

        var after = await provider.GetPaymentIntentAsync(created.ProviderIntentId);
        after.Should().Be(created);
    }

    [Fact]
    public async Task RefundPartialToken_AllowsAPartialRefund()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.RefundPartialToken);

        var refunded = await provider.RefundAsync(
            RefundRequest(created.ProviderIntentId, new Money(created.Amount.AmountMinor / 2, "LKR")));

        refunded.Status.Should().Be(PaymentProviderStatus.Succeeded);
        refunded.Amount.AmountMinor.Should().Be(created.Amount.AmountMinor / 2);
        refunded.ProviderIntentId.Should().Be(created.ProviderIntentId);
        refunded.ProviderRefundId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refund_FullSettledAmount_Succeeds()
    {
        // M14's provider half: the ledger `Refund` entry and the reconciliation assertion are
        // settlement-service concerns (P2-B2); the provider must accept the full-amount refund.
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.SucceedToken);

        var refunded = await provider.RefundAsync(RefundRequest(created.ProviderIntentId, created.Amount));

        refunded.Status.Should().Be(PaymentProviderStatus.Succeeded);
        refunded.Amount.Should().Be(created.Amount);
        refunded.ProviderIntentId.Should().Be(created.ProviderIntentId);
    }

    [Fact]
    public async Task Refund_ReplayedIdempotencyKey_ReturnsTheOriginalRefund()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.SucceedToken);
        var request = RefundRequest(created.ProviderIntentId, new Money(100, "LKR"));

        var first = await provider.RefundAsync(request);
        var replay = await provider.RefundAsync(request);

        replay.Should().Be(first);
    }

    [Fact]
    public async Task Refund_UnknownIntent_ThrowsStateException()
    {
        var provider = Create();

        var refund = () => provider.RefundAsync(RefundRequest("mock_does_not_exist", Money.Lkr(1m)));

        await refund.Should().ThrowAsync<PaymentDomainException>();
    }

    [Fact]
    public async Task Refund_UnsettledIntent_ThrowsStateException()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.PendingToken);

        var refund = () => provider.RefundAsync(RefundRequest(created.ProviderIntentId, Money.Lkr(1m)));

        await refund.Should().ThrowAsync<PaymentIntentStateException>();
    }

    [Fact]
    public async Task Refund_BeyondTheSettledAmount_ThrowsStateException()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.SucceedToken);

        var refund = () => provider.RefundAsync(
            RefundRequest(created.ProviderIntentId, new Money(created.Amount.AmountMinor + 1, "LKR")));

        await refund.Should().ThrowAsync<PaymentIntentStateException>();
    }

    // ============================================================ §7.3 guards

    [Fact]
    public async Task Create_NonPositiveAmount_ThrowsNotSupported()
    {
        var provider = Create();

        var zero = () => provider.CreatePaymentIntentAsync(Request(amount: new Money(0, "LKR")));
        var negative = () => provider.CreatePaymentIntentAsync(Request(amount: new Money(-1, "LKR")));

        await zero.Should().ThrowAsync<PaymentProviderNotSupportedException>();
        await negative.Should().ThrowAsync<PaymentProviderNotSupportedException>();
    }

    [Fact]
    public async Task Create_WrongCurrency_ThrowsNotSupported()
    {
        var provider = Create();

        var create = () => provider.CreatePaymentIntentAsync(Request(amount: new Money(350000, "USD")));

        await create.Should().ThrowAsync<PaymentProviderNotSupportedException>();
    }

    [Fact]
    public async Task CheckoutUrl_UsesTheConfiguredRelativeBase_AndNeverFabricatesAHost()
    {
        var provider = Create();
        var request = Request();

        var intent = await provider.CreatePaymentIntentAsync(
            request, MockPaymentProvider.RequiresActionToken);

        intent.CheckoutUrl.Should().NotBeNull();
        var url = intent.CheckoutUrl!.ToString();

        url.Should().StartWith("/api/v1/dev/mock-checkout/");
        url.Should().EndWith(request.IntentId.ToString());
        url.Should().NotContain("http", "the default base is relative, so no host may be invented");
        url.Should().NotContain("aveline.boutique");
        intent.CheckoutUrl.IsAbsoluteUri.Should().BeFalse();
    }

    [Fact]
    public async Task CheckoutUrl_WithAnAbsoluteConfiguredBase_StaysAbsolute()
    {
        var options = Configured(mock =>
            mock.CheckoutBaseUrl = "https://demo.aveline.test/api/v1/dev/mock-checkout");
        var provider = Create(options: options);
        var request = Request();

        var intent = await provider.CreatePaymentIntentAsync(
            request, MockPaymentProvider.RequiresActionToken);

        intent.CheckoutUrl.Should().NotBeNull();
        intent.CheckoutUrl!.IsAbsoluteUri.Should().BeTrue();
        intent.CheckoutUrl.ToString().Should()
            .Be($"https://demo.aveline.test/api/v1/dev/mock-checkout/{request.IntentId}");
    }

    // ============================================================ subscriptions

    [Fact]
    public async Task Subscription_CreateOrUpdate_IsDeterministic()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var provider = Create(clock: new FixedTimeProvider(now));
        var request = new CreateProviderSubscriptionRequest(
            ProviderSubscriptionId: null,
            ProviderCustomerId: "org-mock-tests",
            RecurringAmount: Money.Lkr(3500m),
            Interval: "month",
            IdempotencyKey: $"sub-{Guid.NewGuid():N}",
            AllowedPaymentMethodTypes: null);

        var first = await provider.CreateOrUpdateSubscriptionAsync(request);
        var again = await provider.CreateOrUpdateSubscriptionAsync(request with { IdempotencyKey = request.IdempotencyKey });

        again.ProviderSubscriptionId.Should().Be(first.ProviderSubscriptionId);
        first.RecurringAmount.Should().Be(Money.Lkr(3500m));
        first.Interval.Should().Be("month");
        first.CurrentPeriodEnd.Should().Be(now.UtcDateTime.AddMonths(1));
        first.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public async Task Subscription_CancelAtPeriodEnd_IsHonoured()
    {
        var provider = Create();
        var created = await provider.CreateOrUpdateSubscriptionAsync(new CreateProviderSubscriptionRequest(
            ProviderSubscriptionId: null,
            ProviderCustomerId: "org-mock-tests",
            RecurringAmount: Money.Lkr(3500m),
            Interval: "month",
            IdempotencyKey: $"sub-{Guid.NewGuid():N}",
            AllowedPaymentMethodTypes: null));

        var scheduled = await provider.CancelSubscriptionAsync(created.ProviderSubscriptionId, atPeriodEnd: true);
        var immediate = await provider.CancelSubscriptionAsync(created.ProviderSubscriptionId, atPeriodEnd: false);

        scheduled.CancelAtPeriodEnd.Should().BeTrue();
        scheduled.Status.Should().Be(PaymentProviderStatus.Processing);
        immediate.Status.Should().Be(PaymentProviderStatus.Cancelled);
    }

    [Fact]
    public async Task Subscription_CancelUnknown_ThrowsStateException()
    {
        var provider = Create();

        var cancel = () => provider.CancelSubscriptionAsync("mock_sub_absent", atPeriodEnd: true);

        await cancel.Should().ThrowAsync<PaymentIntentStateException>();
    }

    // ============================================================ webhooks

    [Fact]
    public async Task Webhook_SignedBody_VerifiesAndParses()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var provider = Create(clock: new FixedTimeProvider(now));
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.SucceedToken);

        var payload = provider.BuildWebhook(created.ProviderIntentId);
        var parsed = provider.VerifyAndParseWebhook(payload.ToRequest(now));

        parsed.ProviderEventId.Should().NotBeNullOrWhiteSpace();
        parsed.Type.Should().Be(PaymentWebhookEventType.IntentSucceeded);
        parsed.ProviderIntentId.Should().Be(created.ProviderIntentId);
        parsed.Amount.Should().Be(created.Amount);
        parsed.OccurredAt.Should().Be(now);
    }

    [Fact]
    public void Webhook_MissingSignatureHeader_IsRejected()
    {
        var provider = Create();
        var request = new PaymentWebhookRequest(
            "{\"id\":\"evt_x\",\"type\":\"intent.succeeded\"}",
            new Dictionary<string, string> { [MockPaymentProvider.TimestampHeader] = "1750000000" },
            "203.0.113.9",
            DateTimeOffset.UtcNow);

        var verify = () => provider.VerifyAndParseWebhook(request);

        verify.Should().Throw<PaymentWebhookVerificationException>();
    }

    [Fact]
    public void Webhook_BadSignature_IsRejected()
    {
        var provider = Create();
        var request = new PaymentWebhookRequest(
            "{\"id\":\"evt_x\",\"type\":\"intent.succeeded\"}",
            new Dictionary<string, string>
            {
                [MockPaymentProvider.SignatureHeader] = "v1,not-a-valid-digest",
                [MockPaymentProvider.TimestampHeader] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            },
            "203.0.113.9",
            DateTimeOffset.UtcNow);

        var verify = () => provider.VerifyAndParseWebhook(request);

        verify.Should().Throw<PaymentWebhookVerificationException>();
    }

    [Fact]
    public void Webhook_StaleTimestamp_IsRejectedEvenWhenTheHmacMatches()
    {
        var now = DateTimeOffset.UtcNow;
        var provider = Create(clock: new FixedTimeProvider(now));
        const string body = "{\"id\":\"evt_stale\",\"type\":\"intent.succeeded\"}";

        var stale = new PaymentWebhookRequest(
            body,
            MockPaymentProvider.SignedHeaders(Secret, now.AddMinutes(-10), body),
            "203.0.113.9",
            now);

        var verify = () => provider.VerifyAndParseWebhook(stale);

        verify.Should().Throw<PaymentWebhookVerificationException>();
    }

    [Fact]
    public void Webhook_SignatureIsStableForAFixedBodyAndSecret()
    {
        const string body = "{\"id\":\"evt_fixed\",\"type\":\"intent.succeeded\"}";
        const long unixSeconds = 1789000000;

        var first = MockPaymentProvider.SignBody(Secret, unixSeconds, body);
        var second = MockPaymentProvider.SignBody(Secret, unixSeconds, body);

        second.Should().Be(first);
        first.Should().StartWith("v1,");
        first.Should().Be(
            "v1," + Convert.ToBase64String(
                System.Security.Cryptography.HMACSHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("mock-unit-test-secret"),
                    System.Text.Encoding.UTF8.GetBytes($"{unixSeconds}.{body}"))),
            "the scheme is HMAC-SHA256 over '{timestamp}.{body}' with the whsec_ prefix stripped");
    }

    [Fact]
    public void Webhook_Base64Secret_AlsoVerifies()
    {
        var secret = "whsec_" + Convert.ToBase64String("another-mock-secret"u8.ToArray());
        var options = Configured(mock => mock.WebhookSigningSecret = secret);
        var provider = Create(options: options);
        const string body = "{\"id\":\"evt_b64\",\"type\":\"intent.succeeded\"}";
        var now = DateTimeOffset.UtcNow;

        var parsed = provider.VerifyAndParseWebhook(new PaymentWebhookRequest(
            body,
            MockPaymentProvider.SignedHeaders(secret, now, body),
            "203.0.113.9",
            now));

        parsed.ProviderEventId.Should().Be("evt_b64");
        parsed.Type.Should().Be(PaymentWebhookEventType.IntentSucceeded);
    }

    /// <summary>
    /// P10 (plan §9.6): a dispute is no longer lumped in with the unrecognised types. The adapter maps
    /// it to the explicit <see cref="PaymentWebhookEventType.DisputeOpened"/>, whose settlement effect
    /// is an append-only Revenue reversal.
    /// </summary>
    [Fact]
    public void Webhook_ADisputeEvent_MapsToTheExplicitDisputeType()
    {
        var provider = Create();
        var now = DateTimeOffset.UtcNow;
        const string body =
            "{\"id\":\"evt_dispute_1\",\"type\":\"charge.disputed\",\"intentId\":\"mock_x\","
            + "\"amountMinor\":900000,\"currency\":\"LKR\"}";

        var parsed = provider.VerifyAndParseWebhook(new PaymentWebhookRequest(
            body,
            MockPaymentProvider.SignedHeaders(Secret, now, body),
            "203.0.113.9",
            now));

        parsed.Type.Should().Be(PaymentWebhookEventType.DisputeOpened);
        parsed.ProviderEventId.Should().Be("evt_dispute_1");
    }

    /// <summary>
    /// The fail-safe that must survive P10: a genuinely unrecognised type still maps to
    /// <c>Unknown</c>, which the settlement path stores **unprocessed** and logs. It must not be
    /// quietly dropped, and it must not be marked processed with no effect.
    /// </summary>
    [Fact]
    public void Webhook_AnUnrecognisedEventType_MapsToUnknown()
    {
        var provider = Create();
        var now = DateTimeOffset.UtcNow;
        const string body =
            "{\"id\":\"evt_unknown_1\",\"type\":\"some.unknown.event\",\"intentId\":\"mock_x\","
            + "\"amountMinor\":900000,\"currency\":\"LKR\"}";

        var parsed = provider.VerifyAndParseWebhook(new PaymentWebhookRequest(
            body,
            MockPaymentProvider.SignedHeaders(Secret, now, body),
            "203.0.113.9",
            now));

        parsed.Type.Should().Be(PaymentWebhookEventType.Unknown);
        parsed.ProviderEventId.Should().Be("evt_unknown_1");
    }

    [Fact]
    public void Webhook_VerificationFailure_IncrementsTheMetric()
    {
        using var metrics = new PaymentMetrics();
        var provider = Create(metrics: metrics);
        var request = new PaymentWebhookRequest(
            "{}",
            new Dictionary<string, string>(),
            "203.0.113.9",
            DateTimeOffset.UtcNow);

        var verify = () => provider.VerifyAndParseWebhook(request);
        verify.Should().Throw<PaymentWebhookVerificationException>();

        metrics.VerificationFailureCount("mock", "missing").Should().Be(1);
    }

    // ============================================================ scenario token emission

    [Fact]
    public async Task DuplicateEventToken_EmitsTheSameEventIdTwice()
    {
        // The clock is part of the determinism contract (§7.3), so a fixed clock makes "the same
        // event" a byte-for-byte replay of the delivery the inbox must recognise.
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var provider = Create(clock: new FixedTimeProvider(now));
        var created = await provider.CreatePaymentIntentAsync(
            Request(), MockPaymentProvider.DuplicateEventToken);

        var first = provider.BuildWebhook(created.ProviderIntentId);
        var second = provider.BuildWebhook(created.ProviderIntentId);

        second.Body.Should().Be(first.Body);
        second.Headers[MockPaymentProvider.SignatureHeader]
            .Should().Be(first.Headers[MockPaymentProvider.SignatureHeader]);
        provider.VerifyAndParseWebhook(first.ToRequest(now)).ProviderEventId
            .Should().Be(provider.VerifyAndParseWebhook(second.ToRequest(now)).ProviderEventId);
    }

    [Fact]
    public async Task LateEventToken_CarriesATenMinuteOldTimestamp()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var provider = Create(clock: new FixedTimeProvider(now));
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.LateEventToken);

        var payload = provider.BuildWebhook(created.ProviderIntentId);
        var timestamp = long.Parse(payload.Headers[MockPaymentProvider.TimestampHeader]);

        timestamp.Should().Be(now.AddMinutes(-10).ToUnixTimeSeconds());

        var verify = () => provider.VerifyAndParseWebhook(payload.ToRequest(now));
        verify.Should().Throw<PaymentWebhookVerificationException>();
    }

    [Fact]
    public async Task AmountMismatchToken_EmitsAnEventForOneMinorUnitMore()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(
            Request(), MockPaymentProvider.AmountMismatchToken);

        var parsed = provider.VerifyAndParseWebhook(
            provider.BuildWebhook(created.ProviderIntentId).ToRequest(DateTimeOffset.UtcNow));

        parsed.Amount.Should().NotBeNull();
        parsed.Amount!.Value.AmountMinor.Should().Be(created.Amount.AmountMinor + 1);
    }

    [Fact]
    public async Task CurrencyMismatchToken_EmitsAnEventInUsd()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(
            Request(), MockPaymentProvider.CurrencyMismatchToken);

        var parsed = provider.VerifyAndParseWebhook(
            provider.BuildWebhook(created.ProviderIntentId).ToRequest(DateTimeOffset.UtcNow));

        parsed.Amount.Should().NotBeNull();
        parsed.Amount!.Value.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task BadSignatureToken_OmitsTheSignatureHeader()
    {
        var provider = Create();
        var created = await provider.CreatePaymentIntentAsync(Request(), MockPaymentProvider.BadSignatureToken);

        var payload = provider.BuildWebhook(created.ProviderIntentId);

        payload.Headers.Should().NotContainKey(MockPaymentProvider.SignatureHeader);
        var verify = () => provider.VerifyAndParseWebhook(payload.ToRequest(DateTimeOffset.UtcNow));
        verify.Should().Throw<PaymentWebhookVerificationException>();
    }

    // ============================================================ §7.3 proration

    /// <summary>
    /// <c>round(monthlyPrice * remainingDays / daysInMonth, 2)</c>, including a leap February and a
    /// zero-day remainder. The half-cent case is <c>AwayFromZero</c>, matching <c>Money.Lkr</c>.
    /// </summary>
    [Theory]
    [InlineData(3500, 31, 31, 3500.00)]
    [InlineData(3500, 15, 30, 1750.00)]
    [InlineData(3500, 1, 29, 120.69)]
    [InlineData(3500, 28, 29, 3379.31)]
    [InlineData(9000, 10, 31, 2903.23)]
    [InlineData(3500, 0, 31, 0.00)]
    [InlineData(20000, 0, 28, 0.00)]
    [InlineData(0.05, 1, 2, 0.03)]
    public void Proration_MatchesTheDocumentedTable(
        decimal monthlyPrice, int remainingDays, int daysInMonth, decimal expected)
    {
        MockPaymentProvider.ProrateMonthly(monthlyPrice, remainingDays, daysInMonth)
            .Should().Be(expected);
    }

    [Fact]
    public void Proration_FromThePeriodEnd_UsesTheCalendarMonthOfTheProrationDate()
    {
        // 2028 is a leap year, so February has 29 days.
        DateTime.DaysInMonth(2028, 2).Should().Be(29);

        MockPaymentProvider.ProrateMonthly(3500m, new DateOnly(2028, 2, 29), new DateOnly(2028, 2, 1))
            .Should().Be(3379.31m);
        MockPaymentProvider.ProrateMonthly(3500m, new DateOnly(2028, 2, 29), new DateOnly(2028, 2, 28))
            .Should().Be(120.69m);
    }

    [Fact]
    public void Proration_OnThePeriodEndDate_HasAZeroDayRemainder()
    {
        MockPaymentProvider.ProrateMonthly(3500m, new DateOnly(2028, 2, 29), new DateOnly(2028, 2, 29))
            .Should().Be(0.00m);
        MockPaymentProvider.ProrateMonthly(3500m, new DateOnly(2027, 2, 28), new DateOnly(2027, 2, 28))
            .Should().Be(0.00m);
    }

    [Fact]
    public void Proration_NonLeapFebruary_UsesTwentyEightDays()
    {
        DateTime.DaysInMonth(2027, 2).Should().Be(28);

        MockPaymentProvider.ProrateMonthly(3500m, new DateOnly(2027, 2, 28), new DateOnly(2027, 2, 1))
            .Should().Be(3375.00m);
    }

    // ============================================================ §7.3 logging

    [Fact]
    public void Construction_LogsTheMockWarningOnce()
    {
        var logger = new CapturingLogger<MockPaymentProvider>();

        _ = Create(logger: logger);

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain(
            "MOCK PAYMENT PROVIDER ACTIVE: no real money can be collected in this process.");
    }

    [Fact]
    public async Task CreateWithAToken_LogsOneInformationLinePerTransition()
    {
        var logger = new CapturingLogger<MockPaymentProvider>();
        var provider = Create(logger: logger);
        var request = Request();

        _ = await provider.CreatePaymentIntentAsync(request, MockPaymentProvider.SucceedToken);
        // A replay is not a transition, so it must not log a second line.
        _ = await provider.CreatePaymentIntentAsync(request, MockPaymentProvider.SucceedToken);

        var transitions = logger.Entries.Where(entry => entry.Level == LogLevel.Information).ToArray();
        transitions.Should().ContainSingle();
        transitions[0].Message.Should().Contain(request.IntentId.ToString());
        transitions[0].Message.Should().Contain(request.CustomerReference);
        transitions[0].Message.Should().Contain("mock");
        transitions[0].Message.Should().Contain(MockPaymentProvider.SucceedToken);
    }

    // ============================================================ §7.5 M19 (C11)

    [Fact]
    public void M19_NoPaymentModelOrDtoCanCarryCardData()
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "card", "cardnumber", "pan", "maskedpan", "iin", "last4",
            "cvc", "cvv", "cvv2", "securitycode", "pin",
            "cardholder", "cardholdername", "expiry", "expirymonth", "expiryyear",
            "token", "cardtoken", "paymenttoken", "sourcetoken", "trackdata",
        };

        var prefixes = new[]
        {
            "Aveline.Api.Modules.Payments.Domain",
            "Aveline.Api.Modules.Payments.Models",
            "Aveline.Api.Modules.Payments.DTOs",
        };

        var types = typeof(PaymentIntent).Assembly.GetTypes()
            .Where(type => type.Namespace is not null
                && prefixes.Any(prefix => type.Namespace == prefix
                    || type.Namespace.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .ToArray();

        types.Should().NotBeEmpty("the scan must actually look at the payment models and DTOs");

        var offenders = types
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => forbidden.Contains(property.Name))
                .Select(property => $"{type.FullName}.{property.Name}"))
            .ToArray();

        offenders.Should().BeEmpty(
            "constraint C11: no persisted model or wire DTO may carry a card, PAN, CVC, expiry, or token field");
    }

    // ============================================================ §12.1 metrics

    [Fact]
    public void PaymentMetrics_CreatesEverySection12Instrument()
    {
        using var listener = new MeterListener();
        var published = new List<string>();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName)
            {
                published.Add(instrument.Name);
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.Start();

        using var metrics = new PaymentMetrics();

        published.Should().Contain(Section12Instruments);
    }

    [Fact]
    public void MockProviderActiveGauge_ReportsOneForTheMockKey()
    {
        using var listener = new MeterListener();
        var measurements = new List<(string Provider, double Value)>();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Name == "aveline.payment.mock_provider_active")
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
        {
            string? provider = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "provider")
                {
                    provider = tag.Value?.ToString();
                }
            }

            measurements.Add((provider ?? string.Empty, value));
        });
        listener.Start();

        using var metrics = new PaymentMetrics();
        metrics.IsMockProviderActive("mock").Should().BeFalse();

        // Constructing the adapter is what activates the gauge (guardrail 4 of plan §7.4).
        _ = Create(metrics: metrics);
        metrics.IsMockProviderActive("mock").Should().BeTrue();

        listener.RecordObservableInstruments();

        measurements.Should().Contain(measurement =>
            measurement.Provider == "mock" && measurement.Value == 1d);
    }

    [Fact]
    public void AddPaymentsModule_PrimesTheMockGaugeFromConfiguration()
    {
        using var enabled = BuildModule("true");
        using var disabled = BuildModule(null);

        enabled.GetRequiredService<PaymentMetrics>().IsMockProviderActive("mock").Should().BeTrue();
        disabled.GetRequiredService<PaymentMetrics>().IsMockProviderActive("mock").Should().BeFalse();
    }

    [Fact]
    public void AddPaymentsModule_RegistersTheMockAdapterForTheFactory()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Provider"] = "mock",
                ["Payments:Mock:Enabled"] = "true",
                ["Payments:Mock:AutoSettle"] = "true",
                ["Payments:Mock:WebhookSigningSecret"] = Secret,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.AddPaymentsModule(configuration, Environment(Environments.Development))
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

        factory.Active.Key.Should().Be("mock");
        factory.Active.Capabilities.Should().Be(new PaymentProviderCapabilities(
            SupportsRecurringSubscriptions: true,
            SupportsProration: true,
            SupportsPartialRefunds: true,
            SupportsCancelAtPeriodEnd: true,
            SupportsHostedCheckout: true,
            SettlesAsynchronously: true));
    }

    // ------------------------------------------------------------ helpers

    private static ServiceProvider BuildModule(string? mockEnabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["Payments:Mock:WebhookSigningSecret"] = Secret,
        };

        if (mockEnabled is not null)
        {
            values["Payments:Mock:Enabled"] = mockEnabled;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        return services
            .AddPaymentsModule(configuration, Environment(Environments.Development))
            .BuildServiceProvider();
    }

    private static IHostEnvironment Environment(string name) => new StubHostEnvironment(name);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Aveline.Api.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
