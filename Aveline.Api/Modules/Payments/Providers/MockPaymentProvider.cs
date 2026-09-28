using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Payments.Providers;

/// <summary>
/// The mock adapter (plan §7). A first-class <see cref="IPaymentProvider"/> — not a test double —
/// that behaves as tightly as a real provider so every flow in the plan can be demonstrated with no
/// network access and no external credentials.
/// </summary>
/// <remarks>
/// <para>
/// <b>Credential model.</b> A closed table of test credentials is accepted as an <em>input string</em>
/// only: the eleven Stripe public test cards and their documented <c>pm_card_*</c>/<c>tok_*</c>
/// synonyms (so a demonstration script keeps working after a swap to Stripe test mode), plus twelve
/// <b>Aveline-specific</b> <c>tok_aveline_*</c> scenario tokens with no Stripe counterpart. No
/// credential is ever a property of a persisted model or a wire DTO (constraint C11). A value
/// outside the table is refused with <see cref="UnknownTestCredentialException"/>
/// (<c>400 unknown-test-credential</c>).
/// </para>
/// <para>
/// <b>Anti-production.</b> Guardrail 3 of plan §7.4: construction logs a warning. The startup guard
/// (<c>PaymentsOptions.Validate</c>), the factory's <c>Mock.Enabled</c> check, the dev-only
/// endpoints and the <c>aveline.payment.mock_provider_active</c> gauge are the other five.
/// </para>
/// <para>
/// <b>State.</b> Like <see cref="ManualPaymentProvider"/>, the adapter keeps its provider-side state
/// in the scoped instance: an intent store keyed by provider intent id and an idempotency index keyed
/// by <c>IdempotencyKey</c>. The durable dedup identity remains the intent row's filtered unique
/// index, not this cache.
/// </para>
/// </remarks>
internal sealed class MockPaymentProvider : IPaymentProvider, IProrationProvider, IPaymentRefundReader
{
    public const string ProviderKey = "mock";

    /// <summary>The HMAC signature header, named after the Clerk scheme's <c>svix-signature</c>.</summary>
    public const string SignatureHeader = "X-Aveline-Signature";

    /// <summary>The Unix-seconds timestamp header, the replay guard.</summary>
    public const string TimestampHeader = "X-Aveline-Timestamp";

    // --- Aveline-specific scenario tokens (plan §7.2). The token form is the preferred input. ---

    /// <summary>Happy path: settles in place when <c>AutoSettle</c> is true.</summary>
    public const string SucceedToken = "tok_aveline_succeed";

    /// <summary>Asynchronous settlement: <c>Processing</c> until the settle endpoint or a webhook.</summary>
    public const string PendingToken = "tok_aveline_pending";

    /// <summary>Hosted-page redirect and client polling.</summary>
    public const string RequiresActionToken = "tok_aveline_requires_action";

    /// <summary><c>RequiresAction</c> with an expiry 60 seconds out.</summary>
    public const string ExpireToken = "tok_aveline_expire";

    /// <summary>The unknown-outcome path: creation raises a transport failure.</summary>
    public const string TimeoutToken = "tok_aveline_timeout";

    /// <summary>Settles, then emits the same webhook event twice (the inbox's replay guard).</summary>
    public const string DuplicateEventToken = "tok_aveline_duplicate_event";

    /// <summary>Settles, then emits an event whose timestamp is ten minutes old.</summary>
    public const string LateEventToken = "tok_aveline_late_event";

    /// <summary>Settles, then emits an event for one minor unit more than the intent.</summary>
    public const string AmountMismatchToken = "tok_aveline_amount_mismatch";

    /// <summary>Settles, then emits an event in USD.</summary>
    public const string CurrencyMismatchToken = "tok_aveline_currency_mismatch";

    /// <summary>Settles, then emits an event with no signature header.</summary>
    public const string BadSignatureToken = "tok_aveline_bad_signature";

    /// <summary>Settles, then allows a refund of half the amount.</summary>
    public const string RefundPartialToken = "tok_aveline_refund_partial";

    /// <summary>Settles, then refuses every refund with a provider error (decision D8).</summary>
    public const string RefundFailToken = "tok_aveline_refund_fail";

    private const string SignaturePrefix = "v1,";
    private const string SecretPrefix = "whsec_";
    private const string DefaultCheckoutBaseUrl = "/api/v1/dev/mock-checkout";
    private const string HostedCheckoutPlaceholder = "(hosted-checkout)";

    private static readonly TimeSpan ExpiryWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LateEventOffset = TimeSpan.FromMinutes(-10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ------------------------------------------------------------ the closed credential table

    /// <summary>
    /// The eleven Stripe public test cards of plan §7.2. Card numbers are Stripe's documented public
    /// test values, used deliberately so a demonstration script survives a swap to Stripe test mode.
    /// </summary>
    private static readonly IReadOnlyList<CardDefinition> CardDefinitions =
    [
        new(
            "4242424242424242", PaymentProviderStatus.Succeeded, null, null, DeclinesOnSettle: false,
            ["pm_card_visa", "pm_card_mastercard", "tok_visa", "tok_visa_debit", "tok_mastercard"]),
        new(
            "4000000000000002", PaymentProviderStatus.Failed, "card_declined", "generic_decline", false,
            ["pm_card_visa_chargeDeclined", "tok_visa_chargeDeclined"]),
        new(
            "4000000000009995", PaymentProviderStatus.Failed, "card_declined", "insufficient_funds", false,
            ["pm_card_visa_chargeDeclinedInsufficientFunds", "tok_visa_chargeDeclinedInsufficientFunds"]),
        new(
            "4000000000009987", PaymentProviderStatus.Failed, "card_declined", "lost_card", false,
            ["pm_card_visa_chargeDeclinedLostCard"]),
        new(
            "4000000000009979", PaymentProviderStatus.Failed, "card_declined", "stolen_card", false,
            ["pm_card_visa_chargeDeclinedStolenCard"]),
        new(
            "4000000000000069", PaymentProviderStatus.Failed, "expired_card", null, false,
            ["pm_card_visa_chargeDeclinedExpiredCard"]),
        new(
            "4000000000000127", PaymentProviderStatus.Failed, "incorrect_cvc", null, false,
            ["pm_card_visa_chargeDeclinedIncorrectCvc"]),
        new(
            "4000000000000119", PaymentProviderStatus.Failed, "processing_error", null, false,
            ["pm_card_visa_chargeDeclinedProcessingError"]),
        new(
            "4242424242424241", PaymentProviderStatus.Failed, "incorrect_number", null, false,
            ["pm_card_visa_chargeDeclinedIncorrectNumber"]),
        new(
            "4000000000006975", PaymentProviderStatus.Failed, "card_declined", "card_velocity_exceeded", false,
            ["pm_card_visa_chargeDeclinedVelocityExceeded"]),
        // Succeeds when the payment method is attached, then declines when the charge is taken. Stripe
        // documents the behaviour but not a public PaymentMethod identifier for it, so it has no synonym.
        new(
            "4000000000000341", PaymentProviderStatus.Succeeded, null, null, DeclinesOnSettle: true,
            [], "card_declined", "generic_decline"),
    ];

    private static readonly IReadOnlyList<MockCredential> ScenarioCredentials =
    [
        Scenario(SucceedToken, PaymentProviderStatus.Succeeded),
        Scenario(PendingToken, PaymentProviderStatus.Processing),
        Scenario(RequiresActionToken, PaymentProviderStatus.RequiresAction),
        Scenario(ExpireToken, PaymentProviderStatus.RequiresAction),
        // tok_aveline_timeout raises a transport failure at create, so its status is never observed.
        Scenario(TimeoutToken, PaymentProviderStatus.Processing),
        Scenario(DuplicateEventToken, PaymentProviderStatus.Succeeded),
        Scenario(LateEventToken, PaymentProviderStatus.Succeeded),
        Scenario(AmountMismatchToken, PaymentProviderStatus.Succeeded),
        Scenario(CurrencyMismatchToken, PaymentProviderStatus.Succeeded),
        Scenario(BadSignatureToken, PaymentProviderStatus.Succeeded),
        Scenario(RefundPartialToken, PaymentProviderStatus.Succeeded),
        Scenario(RefundFailToken, PaymentProviderStatus.Succeeded),
    ];

    private static readonly IReadOnlyDictionary<string, MockCredential> CredentialTable = BuildTable();

    /// <summary>Every accepted Stripe-compatible card number, in table order.</summary>
    internal static IReadOnlyList<string> CardNumbers { get; } =
        CardDefinitions.Select(definition => definition.CardNumber).ToArray();

    /// <summary>Every accepted <c>pm_card_*</c>/<c>tok_*</c> synonym.</summary>
    internal static IReadOnlyList<string> CredentialSynonyms { get; } =
        CardDefinitions.SelectMany(definition => definition.Synonyms).ToArray();

    /// <summary>Every Aveline-specific scenario token.</summary>
    internal static IReadOnlyList<string> ScenarioTokens { get; } =
        ScenarioCredentials.Select(credential => credential.Credential).ToArray();

    private readonly PaymentsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MockPaymentProvider> _logger;
    private readonly PaymentMetrics _metrics;
    private readonly string? _defaultCredential;

    private readonly ConcurrentDictionary<string, MockIntentState> _intents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _intentIdByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderRefund> _refundsByIdempotencyKey = new(StringComparer.Ordinal);
    // Keyed by the *charge*, not the Aveline idempotency key, so
    // `IPaymentRefundReader.ListRefundsAsync` can answer "what has been refunded on this charge?"
    // even for a refund this process did not organise (the deliberately-unreconciled case P7 reads).
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ProviderRefund>> _refundsByIntent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderSubscription> _subscriptions = new(StringComparer.Ordinal);

    // Public on an internal type: the DI container's constructor discovery only considers public
    // constructors, exactly as it does for ManualPaymentProvider's implicit one.
    public MockPaymentProvider(
        IOptions<PaymentsOptions> options,
        TimeProvider clock,
        ILogger<MockPaymentProvider> logger,
        PaymentMetrics metrics,
        string? defaultTestCredential = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);

        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _metrics = metrics;
        _defaultCredential = string.IsNullOrWhiteSpace(defaultTestCredential) ? null : defaultTestCredential;

        // Guardrail 4: the gauge is re-asserted here so a directly-constructed adapter reports itself
        // even when the module registration never ran (tests, tooling).
        _metrics.SetMockProviderActive(ProviderKey, true);

        // Guardrail 3 of plan §7.4: exactly once per construction.
        _logger.LogWarning(
            "MOCK PAYMENT PROVIDER ACTIVE: no real money can be collected in this process.");
    }

    public string Key => ProviderKey;

    public PaymentProviderCapabilities Capabilities { get; } = new(
        SupportsRecurringSubscriptions: true,
        SupportsProration: true,
        SupportsPartialRefunds: true,
        SupportsCancelAtPeriodEnd: true,
        SupportsHostedCheckout: true,
        SettlesAsynchronously: true);

    // ------------------------------------------------------------ creation

    public Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
        CreateProviderIntentRequest request, CancellationToken cancellationToken = default) =>
        CreatePaymentIntentAsync(request, _defaultCredential, cancellationToken);

    /// <summary>
    /// Creates an intent with an explicit test credential. The credential is an input string only:
    /// it is never stored on a persisted model or a wire DTO, and the adapter's in-memory state
    /// simply remembers which credential an intent was arranged with.
    /// </summary>
    internal Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
        CreateProviderIntentRequest request, string? testCredential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardAmountAndCurrency(request);

        // Idempotent on the request key: a replay returns the original intent, even when the retry
        // carries a different amount, exactly as a provider with an idempotency store would.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey)
            && _intentIdByIdempotencyKey.TryGetValue(request.IdempotencyKey, out var existingId)
            && _intents.TryGetValue(existingId, out var existing))
        {
            return Task.FromResult(existing.Intent);
        }

        MockCredential? credential = null;
        var supplied = testCredential ?? _defaultCredential;
        if (supplied is not null)
        {
            credential = ResolveCredential(supplied)
                ?? throw Reject(supplied, operation: "create");
        }

        // The demonstration switch: every created intent fails with this code, whatever the credential.
        if (!string.IsNullOrEmpty(_options.Mock.ForceFailureCode))
        {
            return Task.FromResult(Store(
                request,
                credential?.Credential,
                PaymentProviderStatus.Failed,
                _options.Mock.ForceFailureCode,
                failureMessage: null));
        }

        if (credential is not null && credential.Credential == TimeoutToken)
        {
            _metrics.RecordProviderError(ProviderKey, "create", "timeout");
            throw new PaymentProviderTransportException(
                "The mock provider timed out before acknowledging the charge "
                + $"({TimeoutToken}); the outcome is unknown and no intent row may be committed.");
        }

        // No credential yet: the customer has not been to the hosted page, so the adapter reports the
        // hosted-checkout state rather than inventing a settlement.
        if (credential is null)
        {
            return Task.FromResult(Store(
                request,
                credential: null,
                PaymentProviderStatus.RequiresAction,
                failureCode: null,
                failureMessage: null));
        }

        var status = credential.Status;
        if (status == PaymentProviderStatus.Succeeded && !_options.Mock.AutoSettle)
        {
            // Settles in place only when AutoSettle is true (plan §7.3); otherwise the settle
            // endpoint or the webhook is the only path to settlement.
            status = PaymentProviderStatus.RequiresAction;
        }

        return Task.FromResult(Store(
            request, credential.Credential, status, credential.FailureCode, credential.FailureMessage));
    }

    /// <summary>
    /// Applies a test credential to an existing intent: the Development-only settle endpoint's
    /// operation, and the second half of the "succeeds on attach, then declines" card row.
    /// </summary>
    internal Task<ProviderPaymentIntent> ApplyTestCredentialAsync(
        string providerIntentId, string testCredential, CancellationToken cancellationToken = default)
    {
        if (!_intents.TryGetValue(providerIntentId, out var state))
        {
            throw new PaymentIntentStateException(
                $"The mock provider does not know intent '{providerIntentId}'.");
        }

        var credential = ResolveCredential(testCredential)
            ?? throw Reject(testCredential, operation: "settle");

        if (credential.Credential == TimeoutToken)
        {
            _metrics.RecordProviderError(ProviderKey, "settle", "timeout");
            throw new PaymentProviderTransportException(
                "The mock provider timed out while settling "
                + $"({TimeoutToken}); the outcome is unknown.");
        }

        var status = credential.Status;
        var failureCode = credential.FailureCode;
        var failureMessage = credential.FailureMessage;

        if (credential.DeclinesOnSettle)
        {
            status = PaymentProviderStatus.Failed;
            failureCode = credential.SettleFailureCode;
            failureMessage = credential.SettleFailureMessage;
        }

        var next = state.Intent with
        {
            Status = status,
            FailureCode = failureCode,
            FailureMessage = failureMessage,
            CheckoutUrl = status is PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing
                ? state.Intent.CheckoutUrl
                : null,
        };

        state.Credential = credential.Credential;
        state.Intent = next;

        LogTransition(state.Request, credential.Credential, status, failureCode);

        return Task.FromResult(next);
    }

    // ------------------------------------------------------------ reads, cancellation, refunds

    public Task<ProviderPaymentIntent?> GetPaymentIntentAsync(
        string providerIntentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _intents.TryGetValue(providerIntentId, out var state) ? state.Intent : null);

    public Task<ProviderPaymentIntent> CancelPaymentIntentAsync(
        string providerIntentId, string reason, CancellationToken cancellationToken = default)
    {
        if (!_intents.TryGetValue(providerIntentId, out var state))
        {
            throw new PaymentIntentStateException(
                $"The mock provider does not know intent '{providerIntentId}'.");
        }

        if (state.Intent.Status is not (PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing))
        {
            throw new PaymentIntentStateException(
                $"A {state.Intent.Status} intent cannot be cancelled; only an unsettled intent can.");
        }

        var cancelled = state.Intent with
        {
            Status = PaymentProviderStatus.Cancelled,
            CheckoutUrl = null,
            FailureCode = null,
            FailureMessage = null,
        };

        state.Intent = cancelled;
        LogTransition(state.Request, state.Credential, PaymentProviderStatus.Cancelled, failureCode: null);

        return Task.FromResult(cancelled);
    }

    public Task<ProviderRefund> RefundAsync(
        ProviderRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey)
            && _refundsByIdempotencyKey.TryGetValue(request.IdempotencyKey, out var replay))
        {
            return Task.FromResult(replay);
        }

        if (!_intents.TryGetValue(request.ProviderIntentId, out var state))
        {
            throw new PaymentIntentStateException(
                $"The mock provider does not know intent '{request.ProviderIntentId}'.");
        }

        if (state.Credential == RefundFailToken)
        {
            // Decision D8: a provider refusal must leave the intent and the ledger untouched.
            _metrics.RecordRefund(ProviderKey, "provider_error");
            _metrics.RecordProviderError(ProviderKey, "refund", "refund_failed");
            throw new PaymentProviderTransportException(
                "The mock provider refused the refund "
                + $"({RefundFailToken}); no money moved and the intent is unchanged.");
        }

        if (state.Intent.Status != PaymentProviderStatus.Succeeded)
        {
            throw new PaymentIntentStateException(
                $"Only a Succeeded intent can be refunded; '{request.ProviderIntentId}' is "
                + $"{state.Intent.Status}.");
        }

        var remaining = state.Intent.Amount.AmountMinor - state.RefundedMinor;
        if (request.Amount.AmountMinor <= 0 || request.Amount.AmountMinor > remaining)
        {
            throw new PaymentIntentStateException(
                $"The refund of {request.Amount.AmountMinor} minor units is larger than the "
                + $"{remaining} minor units still settled on '{request.ProviderIntentId}'.");
        }

        if (!Capabilities.SupportsPartialRefunds && request.Amount.AmountMinor != state.Intent.Amount.AmountMinor)
        {
            throw new PaymentProviderNotSupportedException(
                "This provider does not support partial refunds.");
        }

        var refund = new ProviderRefund(
            ProviderRefundId: $"mock_rf_{request.ProviderIntentId}_{request.Amount.AmountMinor}",
            ProviderIntentId: request.ProviderIntentId,
            Amount: request.Amount,
            Status: PaymentProviderStatus.Succeeded,
            CreatedAt: _clock.GetUtcNow().UtcDateTime);

        state.RefundedMinor += request.Amount.AmountMinor;
        _refundsByIntent
            .GetOrAdd(request.ProviderIntentId, _ => new ConcurrentQueue<ProviderRefund>())
            .Enqueue(refund);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            _refundsByIdempotencyKey[request.IdempotencyKey] = refund;
        }

        _metrics.RecordRefund(ProviderKey, "succeeded");
        LogTransition(state.Request, state.Credential, PaymentProviderStatus.Succeeded, failureCode: null);

        return Task.FromResult(refund);
    }

    /// <summary>
    /// The mock's refund read (plan §10 Phase 7): every refund it has recorded for a charge, oldest
    /// first. This is what makes a deliberately unreconciled refund visible to the reconciliation
    /// read — the provider knows the money went back and Aveline's row still does not.
    /// </summary>
    public Task<IReadOnlyList<ProviderRefund>> ListRefundsAsync(
        string providerIntentId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProviderRefund> refunds =
            _refundsByIntent.TryGetValue(providerIntentId, out var recorded)
                ? recorded.ToArray()
                : [];

        return Task.FromResult(refunds);
    }

    // ------------------------------------------------------------ webhooks

    /// <summary>
    /// The mock's inbound webhook. The signature is HMAC-SHA256 over <c>{timestamp}.{body}</c> with
    /// <c>Mock.WebhookSigningSecret</c>, carried in <see cref="TimestampHeader"/> and
    /// <see cref="SignatureHeader"/> — the Clerk scheme's shape, including its timestamp tolerance.
    /// </summary>
    public PaymentWebhookEvent VerifyAndParseWebhook(PaymentWebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var headers = request.Headers;
        var signature = GetHeader(headers, SignatureHeader);
        var timestampRaw = GetHeader(headers, TimestampHeader);

        if (string.IsNullOrWhiteSpace(signature))
        {
            throw Fail("missing");
        }

        if (string.IsNullOrWhiteSpace(timestampRaw)
            || !long.TryParse(timestampRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            throw Fail("timestamp");
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var toleranceSeconds = _options.Webhook.ToleranceSeconds > 0 ? _options.Webhook.ToleranceSeconds : 300;
        if ((_clock.GetUtcNow() - sentAt).Duration() > TimeSpan.FromSeconds(toleranceSeconds))
        {
            // Replay guard: a stale signature is refused even when the HMAC matches.
            throw Fail("timestamp");
        }

        var expected = SignBody(_options.Mock.WebhookSigningSecret, unixSeconds, request.RawBody);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expected)))
        {
            throw Fail("signature");
        }

        MockWebhookBody? body;
        try
        {
            body = JsonSerializer.Deserialize<MockWebhookBody>(request.RawBody, Json);
        }
        catch (JsonException)
        {
            throw Fail("payload");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Id))
        {
            throw Fail("payload");
        }

        Money? amount = body.AmountMinor is { } minor
            ? new Money(minor, body.Currency ?? _options.Currency)
            : null;

        return new PaymentWebhookEvent(
            ProviderEventId: body.Id,
            Type: MapEventType(body.Type),
            ProviderIntentId: body.IntentId,
            ProviderRefundId: body.RefundId,
            Amount: amount,
            FailureCode: body.FailureCode,
            OccurredAt: body.OccurredAt ?? sentAt,
            RawPayload: request.RawBody);
    }

    /// <summary>
    /// Builds the webhook the mock "would send" for an intent, honouring the scenario token:
    /// a deterministic event id for the replay case, a ten-minute-old timestamp, a one-unit amount
    /// drift, a USD currency drift, or a missing signature header.
    /// </summary>
    internal MockWebhookPayload BuildWebhook(string providerIntentId, string? testCredential = null)
    {
        if (!_intents.TryGetValue(providerIntentId, out var state))
        {
            throw new PaymentIntentStateException(
                $"The mock provider does not know intent '{providerIntentId}'.");
        }

        var token = testCredential ?? state.Credential;
        var occurredAt = _clock.GetUtcNow();
        if (token == LateEventToken)
        {
            occurredAt = occurredAt.Add(LateEventOffset);
        }

        var amountMinor = state.Intent.Amount.AmountMinor;
        var currency = state.Intent.Amount.Currency;
        if (token == AmountMismatchToken)
        {
            amountMinor += 1;
        }

        if (token == CurrencyMismatchToken)
        {
            currency = "USD";
        }

        var type = state.Intent.Status switch
        {
            PaymentProviderStatus.Succeeded => "intent.succeeded",
            PaymentProviderStatus.Failed => "intent.failed",
            PaymentProviderStatus.Cancelled => "intent.cancelled",
            PaymentProviderStatus.Expired => "intent.expired",
            _ => "intent.processing",
        };

        // Deterministic event id, so delivering it twice is a replay the inbox can recognise.
        var eventId = $"evt_mock_{providerIntentId}_{type}";
        var body = JsonSerializer.Serialize(
            new MockWebhookBody(
                eventId, type, providerIntentId, null, amountMinor, currency, state.Intent.FailureCode, occurredAt),
            Json);

        IReadOnlyDictionary<string, string> headers = token == BadSignatureToken
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TimestampHeader] = occurredAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            }
            : SignedHeaders(_options.Mock.WebhookSigningSecret, occurredAt, body);

        return new MockWebhookPayload(body, headers);
    }

    // ------------------------------------------------------------ subscriptions

    public Task<ProviderSubscription> CreateOrUpdateSubscriptionAsync(
        CreateProviderSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subscriptionId = string.IsNullOrWhiteSpace(request.ProviderSubscriptionId)
            ? StableId("mock_sub_", request.IdempotencyKey)
            : request.ProviderSubscriptionId;

        var periodEnd = request.Interval.Equals("year", StringComparison.OrdinalIgnoreCase)
            ? _clock.GetUtcNow().UtcDateTime.AddYears(1)
            : _clock.GetUtcNow().UtcDateTime.AddMonths(1);

        var subscription = new ProviderSubscription(
            ProviderSubscriptionId: subscriptionId,
            ProviderCustomerId: request.ProviderCustomerId,
            Status: PaymentProviderStatus.Processing,
            RecurringAmount: request.RecurringAmount,
            Interval: request.Interval,
            CurrentPeriodEnd: periodEnd,
            CancelAtPeriodEnd: false);

        _subscriptions[subscriptionId] = subscription;

        return Task.FromResult(subscription);
    }

    public Task<ProviderSubscription> CancelSubscriptionAsync(
        string providerSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(providerSubscriptionId, out var subscription))
        {
            throw new PaymentIntentStateException(
                $"The mock provider does not know subscription '{providerSubscriptionId}'.");
        }

        var updated = atPeriodEnd
            ? subscription with { CancelAtPeriodEnd = true }
            : subscription with { Status = PaymentProviderStatus.Cancelled, CancelAtPeriodEnd = false };

        _subscriptions[providerSubscriptionId] = updated;

        return Task.FromResult(updated);
    }

    // ------------------------------------------------------------ proration (one place)

    /// <summary>
    /// The provider's own answer to <c>SupportsProration</c> (plan §9.3, §8.5): the capability the
    /// adapter advertises is backed by <see cref="ProrateMonthly(decimal, DateOnly, DateOnly)"/>, so
    /// a plan change can let the provider price the charge instead of computing it locally.
    /// </summary>
    public decimal ComputeProration(ProrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ProrateMonthly(
            request.MonthlyDifferenceLkr, request.PeriodEnd, request.At);
    }

    /// <summary>
    /// The mock's deterministic proration: <c>round(monthlyPrice * remainingDays / daysInMonth, 2)</c>,
    /// computed here and nowhere else. A zero-day remainder is <c>0.00</c>.
    /// </summary>
    internal static decimal ProrateMonthly(decimal monthlyPrice, int remainingDays, int daysInMonth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(daysInMonth);

        var days = Math.Clamp(remainingDays, 0, daysInMonth);
        return decimal.Round(monthlyPrice * days / daysInMonth, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Proration from a period end and a proration date. The day count is the proration date's
    /// calendar month, so a leap February bills over 29 days and the remainder is never negative.
    /// </summary>
    internal static decimal ProrateMonthly(decimal monthlyPrice, DateOnly periodEnd, DateOnly at)
    {
        var daysInMonth = DateTime.DaysInMonth(at.Year, at.Month);
        var remainingDays = Math.Clamp(periodEnd.DayNumber - at.DayNumber, 0, daysInMonth);
        return ProrateMonthly(monthlyPrice, remainingDays, daysInMonth);
    }

    // ------------------------------------------------------------ table lookups and signing helpers

    /// <summary>Resolves a credential string to its table row, or null when it is not recognised.</summary>
    internal static MockCredential? ResolveCredential(string? credential)
        => credential is not null && CredentialTable.TryGetValue(credential.Trim(), out var row) ? row : null;

    /// <summary>
    /// HMAC-SHA256 over <c>{timestamp}.{body}</c>. The <c>whsec_</c> prefix is stripped and the
    /// remainder is base64-decoded when it is valid base64 (the Clerk scheme); otherwise it is used
    /// as UTF-8 bytes, so a plain demonstration secret is not silently unusable.
    /// </summary>
    internal static string SignBody(string secret, long unixSeconds, string body)
    {
        var key = ResolveSecretKey(secret);
        var payload = $"{unixSeconds}.{body}";
        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return SignaturePrefix + Convert.ToBase64String(digest);
    }

    /// <summary>Builds the signed header pair for a body at a given instant.</summary>
    internal static Dictionary<string, string> SignedHeaders(string secret, DateTimeOffset at, string body)
    {
        var unixSeconds = at.ToUnixTimeSeconds();
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SignatureHeader] = SignBody(secret, unixSeconds, body),
            [TimestampHeader] = unixSeconds.ToString(CultureInfo.InvariantCulture),
        };
    }

    // ------------------------------------------------------------ internals

    private ProviderPaymentIntent Store(
        CreateProviderIntentRequest request,
        string? credential,
        PaymentProviderStatus status,
        string? failureCode,
        string? failureMessage)
    {
        DateTime? expiresAt = request.ExpiresAt;
        if (credential == ExpireToken)
        {
            expiresAt = _clock.GetUtcNow().UtcDateTime.Add(ExpiryWindow);
        }

        var needsCheckout = status is PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing;

        var intent = new ProviderPaymentIntent(
            ProviderIntentId: $"mock_{request.IntentId:N}",
            Status: status,
            Amount: request.Amount,
            CheckoutUrl: needsCheckout ? BuildCheckoutUrl(request.IntentId) : null,
            ClientSecret: null,
            ExpiresAt: expiresAt,
            FailureCode: failureCode,
            FailureMessage: failureMessage);

        _intents[intent.ProviderIntentId] = new MockIntentState(request, credential, intent);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            _intentIdByIdempotencyKey[request.IdempotencyKey] = intent.ProviderIntentId;
        }

        _metrics.RecordIntentCreated(ProviderKey, request.Purpose.ToString());
        LogTransition(request, credential, status, failureCode);

        return intent;
    }

    private Uri BuildCheckoutUrl(Guid intentId)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.Mock.CheckoutBaseUrl)
            ? DefaultCheckoutBaseUrl
            : _options.Mock.CheckoutBaseUrl.TrimEnd('/');

        // Never a fabricated external host: the host, if any, comes only from configuration.
        return new Uri($"{baseUrl}/{intentId:D}", UriKind.RelativeOrAbsolute);
    }

    private void GuardAmountAndCurrency(CreateProviderIntentRequest request)
    {
        if (request.Amount.AmountMinor <= 0)
        {
            throw new PaymentProviderNotSupportedException(
                "The mock provider cannot create a charge for a non-positive amount.");
        }

        if (!string.Equals(request.Amount.Currency, _options.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentProviderNotSupportedException(
                $"The mock provider is configured for {_options.Currency} but the request was for "
                + $"{request.Amount.Currency}.");
        }
    }

    private UnknownTestCredentialException Reject(string credential, string operation)
    {
        _metrics.RecordProviderError(ProviderKey, operation, "unknown-test-credential");
        return new UnknownTestCredentialException(credential);
    }

    private PaymentWebhookVerificationException Fail(string reason)
    {
        _metrics.RecordWebhookVerificationFailure(ProviderKey, reason);
        return new PaymentWebhookVerificationException(reason);
    }

    private void LogTransition(
        CreateProviderIntentRequest request, string? credential, PaymentProviderStatus status, string? failureCode)
        => _logger.LogInformation(
            "Mock payment state transition. intentId={IntentId} organizationId={OrganizationId} "
            + "provider={Provider} token={Token} status={Status} failureCode={FailureCode}",
            request.IntentId,
            request.CustomerReference,
            ProviderKey,
            credential ?? HostedCheckoutPlaceholder,
            status,
            failureCode);

    private static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        if (headers.TryGetValue(name, out var value))
        {
            return value;
        }

        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static byte[] ResolveSecretKey(string secret)
    {
        var value = secret.StartsWith(SecretPrefix, StringComparison.Ordinal)
            ? secret[SecretPrefix.Length..]
            : secret;

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(value);
        }
    }

    /// <summary>
    /// The mock's inbound wire vocabulary. It is the shared
    /// <see cref="PaymentWebhookEventTypes"/> map (P10) rather than a private switch, so a dispute is
    /// recognised here exactly as an external adapter recognises it, and a name nothing knows still
    /// falls through to <see cref="PaymentWebhookEventType.Unknown"/> for the settlement path's
    /// fail-safe.
    /// </summary>
    private static PaymentWebhookEventType MapEventType(string? type) =>
        PaymentWebhookEventTypes.Parse(type);

    private static IReadOnlyDictionary<string, MockCredential> BuildTable()
    {
        var table = new Dictionary<string, MockCredential>(StringComparer.Ordinal);

        foreach (var definition in CardDefinitions)
        {
            table[definition.CardNumber] = new MockCredential(
                definition.CardNumber,
                definition.Status,
                definition.FailureCode,
                definition.FailureMessage,
                CanonicalCredential: null,
                definition.DeclinesOnSettle,
                IsScenario: false,
                definition.SettleFailureCode,
                definition.SettleFailureMessage);

            foreach (var synonym in definition.Synonyms)
            {
                table[synonym] = new MockCredential(
                    synonym,
                    definition.Status,
                    definition.FailureCode,
                    definition.FailureMessage,
                    CanonicalCredential: definition.CardNumber,
                    definition.DeclinesOnSettle,
                    IsScenario: false,
                    definition.SettleFailureCode,
                    definition.SettleFailureMessage);
            }
        }

        foreach (var scenario in ScenarioCredentials)
        {
            table[scenario.Credential] = scenario;
        }

        return table;
    }

    private static MockCredential Scenario(string credential, PaymentProviderStatus status) =>
        new(
            credential,
            status,
            FailureCode: null,
            FailureMessage: null,
            CanonicalCredential: null,
            DeclinesOnSettle: false,
            IsScenario: true);

    private static string StableId(string prefix, string seed)
        => prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant()[..16];

    private sealed record CardDefinition(
        string CardNumber,
        PaymentProviderStatus Status,
        string? FailureCode,
        string? FailureMessage,
        bool DeclinesOnSettle,
        IReadOnlyList<string> Synonyms,
        string? SettleFailureCode = null,
        string? SettleFailureMessage = null);

    private sealed class MockIntentState(
        CreateProviderIntentRequest request, string? credential, ProviderPaymentIntent intent)
    {
        public CreateProviderIntentRequest Request { get; } = request;

        public string? Credential { get; set; } = credential;

        public ProviderPaymentIntent Intent { get; set; } = intent;

        public long RefundedMinor { get; set; }
    }
}

/// <summary>One row of the mock's closed credential table (plan §7.2).</summary>
/// <param name="Credential">The accepted input string.</param>
/// <param name="Status">The intent status after create with this credential.</param>
/// <param name="FailureCode">The provider failure code, if the row declines.</param>
/// <param name="FailureMessage">The provider failure message (Stripe's decline sub-code), if any.</param>
/// <param name="CanonicalCredential">The card row a synonym resolves to; null for a canonical card or a scenario token.</param>
/// <param name="DeclinesOnSettle">True for the card that succeeds on attach and declines when charged.</param>
/// <param name="IsScenario">True for an Aveline-specific <c>tok_aveline_*</c> token.</param>
/// <param name="SettleFailureCode">The failure code applied when <paramref name="DeclinesOnSettle"/> fires.</param>
/// <param name="SettleFailureMessage">The failure message applied when <paramref name="DeclinesOnSettle"/> fires.</param>
internal sealed record MockCredential(
    string Credential,
    PaymentProviderStatus Status,
    string? FailureCode,
    string? FailureMessage,
    string? CanonicalCredential,
    bool DeclinesOnSettle,
    bool IsScenario,
    string? SettleFailureCode = null,
    string? SettleFailureMessage = null);

/// <summary>A signed webhook body plus the headers the mock would send it with.</summary>
internal sealed record MockWebhookPayload(string Body, IReadOnlyDictionary<string, string> Headers)
{
    internal PaymentWebhookRequest ToRequest(DateTimeOffset receivedAt) =>
        new(Body, Headers, "203.0.113.9", receivedAt);
}

/// <summary>The mock's webhook body shape. No provider SDK type appears here.</summary>
internal sealed record MockWebhookBody(
    string Id,
    string Type,
    string? IntentId,
    string? RefundId,
    long? AmountMinor,
    string? Currency,
    string? FailureCode,
    DateTimeOffset? OccurredAt);
