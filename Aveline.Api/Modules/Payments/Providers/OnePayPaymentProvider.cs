using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Payments.Providers;

/// <summary>
/// The external adapter: OnePay, the Sri Lankan gateway chosen in plan §14 Q5
/// (<see href="https://docs.onepay.lk/api-documentation"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosted checkout only (assumption A2).</b> Every charge is created as a OnePay-hosted
/// redirection page: the adapter sends an amount, a currency, a reference and a return URL, and gets
/// back a <c>redirect_url</c>. The customer types their card details on OnePay's own page, so no card
/// number, CVC, expiry or saved-card token is ever sent to, parsed by, or stored in Aveline. There is
/// deliberately no field for one anywhere in this file.
/// </para>
/// <para>
/// <b>What the published documentation confirms</b> (read this phase at
/// <c>docs.onepay.lk</c>): the base URL <c>https://api.onepay.lk</c>; the create endpoint
/// <c>POST /v3/checkout/link/</c>; its required body fields <c>app_id</c>, <c>amount</c> (a decimal
/// string such as <c>100.00</c>), <c>currency</c>, <c>hash</c>, <c>reference</c>,
/// <c>customer_first_name</c>, <c>customer_last_name</c>, <c>customer_phone_number</c>,
/// <c>customer_email</c> and <c>transaction_redirect_url</c>, with the optional free-form
/// <c>additionalData</c>; the signing rule <c>SHA256(app_id + currency + amount + HASH_SALT)</c>
/// (worked example <c>126ff893…</c>); the refund endpoint <c>POST /v3/transaction/refund/</c> with
/// <c>app_id</c>, <c>onepay_transaction_id</c>, <c>refund_reason</c>, <c>is_partially</c>,
/// <c>amount</c> and <c>refund_note</c>, and its reason-code vocabulary; the callback payload
/// <c>{ transaction_id, status: 1, status_message: "SUCCESS", additional_data }</c>; LKR in the
/// supported-currency list with settlement in LKR (assumption A3, now confirmed for LKR); and the
/// six documented test cards.
/// </para>
/// <para>
/// <b>What is assumed, pending a sandbox account.</b> The published pages name the status read in
/// prose (<c>GET /v3/transaction/status/</c>, "Get Transaction API") without a machine-readable
/// reference, so <em>the status path, the shape of its response, and the full status vocabulary are
/// assumptions</em>, all named as such at the point of use. The mapping
/// (<see cref="MapStatus"/>) is therefore built so that only an explicitly recognised success status
/// becomes <see cref="PaymentProviderStatus.Succeeded"/>, and every unrecognised status falls back to
/// <see cref="PaymentProviderStatus.Processing"/>. A sandbox probe must confirm the path, the
/// response field names and the vocabulary before a live charge is relied on; see the Phase 8
/// sandbox steps in the hand-off notes.
/// </para>
/// <para>
/// <b>Truthful capabilities.</b> OnePay is a redirection/checkout gateway, not a subscription billing
/// system: the Card-on-File surface is an on-demand charge against a saved card, with no provider-side
/// billing period, no proration engine and no period-end cancellation. The adapter therefore declares
/// recurring, proration and cancel-at-period-end false and refuses those calls with
/// <see cref="PaymentProviderNotSupportedException"/>, exactly as plan §8.5 requires ("If the provider
/// has no recurring subscriptions, … Phase 5's renewal becomes create a fresh intent each period").
/// OnePay also publishes no transaction-cancel operation, so
/// <see cref="CancelPaymentIntentAsync"/> refuses too.
/// </para>
/// <para>
/// <b>Webhooks.</b> OnePay's callback is unsigned; across the whole published API reference there is
/// no signature header and no timestamp. The documented integration is a callback for notification
/// plus a status read for verification ("Receive callback / Verify via status API"), and this adapter
/// implements the verifying half (<see cref="GetPaymentIntentAsync"/>). Fabricating an HMAC scheme
/// OnePay does not have would be worse than the honest refusal, so
/// <see cref="VerifyAndParseWebhook"/> throws
/// <see cref="PaymentProviderNotSupportedException"/> and the webhook route answers <c>501</c> for
/// this provider. Settlement therefore arrives by polling the intent read, which is why the client
/// polls and why the plan's S7 is marked not executed.
/// </para>
/// <para>
/// <b>The hash salt and app token are secrets.</b> They are read from configuration, used to sign or
/// authorise a request, and never logged; no exception message in this file interpolates either.
/// </para>
/// </remarks>
internal sealed class OnePayPaymentProvider : IPaymentProvider
{
    /// <summary>The provider key: matches the keyed registration and the <c>Payments:Provider</c> value.</summary>
    public const string ProviderKey = "onepay";

    /// <summary>The named client <c>AddPaymentsModule</c> registers for this adapter.</summary>
    public const string HttpClientName = "payments-onepay";

    private const string DefaultBaseUrl = "https://api.onepay.lk";
    private const string DefaultCheckoutPath = "/v3/checkout/link/";
    private const string DefaultStatusPath = "/v3/transaction/status/{id}";
    private const string DefaultRefundPath = "/v3/transaction/refund/";

    /// <summary>The full-refund reason code OnePay documents; a free-text reason can override it.</summary>
    private const string DefaultRefundReason = "REQUESTED_BY_CUSTOMER";

    /// <summary>
    /// Recognised OnePay transaction statuses, and the Aveline status each means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Confirmed rows:</b> <c>1</c> and <c>SUCCESS</c> are the values in the documented callback
    /// sample (<c>{"status":1,"status_message":"SUCCESS"}</c>), so success is confirmed.
    /// <b>Assumed rows:</b> every other value here is the published vocabulary as read from the
    /// documentation's prose and error tables, not observed from a live transaction. Only this table
    /// can produce <see cref="PaymentProviderStatus.Succeeded"/>; an unlisted value cannot, so a
    /// vocabulary this table does not know can never be mistaken for settled money.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, PaymentProviderStatus> Statuses =
        new Dictionary<string, PaymentProviderStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = PaymentProviderStatus.Succeeded,
            ["SUCCESS"] = PaymentProviderStatus.Succeeded,
            ["SUCCESSFUL"] = PaymentProviderStatus.Succeeded,
            ["COMPLETED"] = PaymentProviderStatus.Succeeded,
            ["PAID"] = PaymentProviderStatus.Succeeded,
            ["REFUNDED"] = PaymentProviderStatus.Succeeded,
            ["PENDING"] = PaymentProviderStatus.Processing,
            ["PROCESSING"] = PaymentProviderStatus.Processing,
            ["INITIATED"] = PaymentProviderStatus.RequiresAction,
            ["CREATED"] = PaymentProviderStatus.RequiresAction,
            ["AWAITING_PAYMENT"] = PaymentProviderStatus.RequiresAction,
            ["FAILED"] = PaymentProviderStatus.Failed,
            ["FAILURE"] = PaymentProviderStatus.Failed,
            ["DECLINED"] = PaymentProviderStatus.Failed,
            ["DO_NOT_HONOUR"] = PaymentProviderStatus.Failed,
            ["CHARGEBACK"] = PaymentProviderStatus.Failed,
            ["CANCELLED"] = PaymentProviderStatus.Cancelled,
            ["CANCELED"] = PaymentProviderStatus.Cancelled,
            ["EXPIRED"] = PaymentProviderStatus.Expired,
        };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OnePayProviderOptions _options;
    private readonly string _defaultCurrency;
    private readonly TimeProvider _clock;
    private readonly ILogger<OnePayPaymentProvider> _logger;
    private readonly PaymentMetrics _metrics;

    /// <summary>
    /// The idempotency index: what this adapter has already created, keyed by Aveline's idempotency
    /// key. OnePay publishes no idempotency-key header, so the provider cannot deduplicate for us;
    /// this in-process record is the adapter's stand-in, matching the manual and mock adapters'
    /// shape. It is not the durable identity - that remains the intent row's filtered unique index -
    /// and it holds no card data, only the provider intent the trace points at.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProviderPaymentIntent> _byIdempotencyKey =
        new(StringComparer.Ordinal);

    public OnePayPaymentProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<PaymentsOptions> options,
        TimeProvider clock,
        ILogger<OnePayPaymentProvider> logger,
        PaymentMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);

        _httpClientFactory = httpClientFactory;
        _options = options.Value.OnePay;
        _defaultCurrency = string.IsNullOrWhiteSpace(options.Value.Currency) ? "LKR" : options.Value.Currency;
        _clock = clock;
        _logger = logger;
        _metrics = metrics;
    }

    public string Key => ProviderKey;

    /// <summary>
    /// What OnePay can actually do. Hosted checkout and partial refunds are real; recurring billing,
    /// proration and period-end cancellation are not part of the published API, so they are false and
    /// the corresponding calls refuse rather than invent a provider-side object. Settlement is
    /// asynchronous because the customer completes the hosted page outside Aveline.
    /// </summary>
    public PaymentProviderCapabilities Capabilities { get; } = new(
        SupportsRecurringSubscriptions: false,
        SupportsProration: false,
        SupportsPartialRefunds: true,
        SupportsCancelAtPeriodEnd: false,
        SupportsHostedCheckout: true,
        SettlesAsynchronously: true);

    /// <summary>
    /// Reads the resolved OnePay configuration and refuses a half-configured adapter at construction.
    /// The same keys are checked at startup by <c>PaymentsOptions.Validate</c>; this guard exists so a
    /// directly-constructed adapter (tests, tooling, a future caller that bypasses the module) fails
    /// with a message rather than an <c>UriFormatException</c> on the first request.
    /// </summary>
    internal static void GuardConfiguration(OnePayProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.AppId))
        {
            throw new InvalidOperationException("Payments:OnePay:AppId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.HashSalt))
        {
            throw new InvalidOperationException("Payments:OnePay:HashSalt is not configured.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "Payments:OnePay:BaseUrl must be an absolute URL, for example https://api.onepay.lk.");
        }
    }

    // ------------------------------------------------------------ creation

    public async Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
        CreateProviderIntentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardConfiguration(_options);
        GuardAmount(request);

        // OnePay requires a return URL it can redirect the customer back to, and it must be HTTPS.
        // An intent without one cannot be completed, so refusing is the truthful answer.
        if (request.ReturnUrl is null)
        {
            throw new PaymentProviderNotSupportedException(
                "The OnePay redirection API requires a transaction_redirect_url (HTTPS) to return "
                + "the customer to; this request carries none.");
        }

        // The idempotency key is what makes a replay traceable. OnePay has no idempotency-key header,
        // so it rides in `reference` (OnePay's own correlation field) and in the metadata.
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentProviderNotSupportedException(
                "The OnePay adapter requires an idempotency key so a replay is not a second charge.");
        }

        // A replay of the same key must not create a second charge. OnePay cannot deduplicate for us,
        // so the adapter returns the intent it already created, exactly as a provider with an
        // idempotency store would - including when the retry carries a different amount.
        if (_byIdempotencyKey.TryGetValue(request.IdempotencyKey, out var existing))
        {
            return existing;
        }

        // The metadata is the trace from an orphaned OnePay transaction back to the Aveline intent.
        var metadata = JsonSerializer.Serialize(
            new AvelineMetadata(
                AvelineIntentId: request.IntentId.ToString("D"),
                AvelineIdempotencyKey: request.IdempotencyKey,
                AvelinePurpose: request.Purpose.ToString(),
                AvelineCustomerReference: request.CustomerReference),
            Json);

        var amount = FormatAmount(request.Amount.AmountMinor);

        var body = new CreateTransactionRequest(
            AppId: _options.AppId,
            Amount: amount,
            Currency: request.Amount.Currency,
            Hash: Sign(_options.AppId, request.Amount.Currency, amount, _options.HashSalt),
            Reference: request.IdempotencyKey,
            CustomerFirstName: _options.CustomerDefaults.FirstName,
            CustomerLastName: _options.CustomerDefaults.LastName,
            CustomerPhoneNumber: _options.CustomerDefaults.PhoneNumber,
            CustomerEmail: _options.CustomerDefaults.Email,
            TransactionRedirectUrl: request.ReturnUrl.AbsoluteUri,
            Metadata: metadata);

        // A create always enters the hosted checkout: the customer completes the charge outside
        // Aveline, so the only honest status at creation is RequiresAction.
        var created = await SendAsync<CreateTransactionResponse>(
            HttpMethod.Post,
            CheckoutPath(),
            body,
            includeAuthorization: false,
            cancellationToken).ConfigureAwait(false);

        var transactionId = created?.Data?.TransactionId;
        var redirectUrl = created?.Data?.RedirectUrl;

        if (string.IsNullOrWhiteSpace(transactionId) || string.IsNullOrWhiteSpace(redirectUrl))
        {
            // The envelope parsed but carried no handoff: treat it as a provider fault rather than
            // inventing an intent the customer cannot complete.
            _metrics.RecordProviderError(ProviderKey, "create", "malformed_response");
            throw new PaymentProviderTransportException(
                "OnePay accepted the checkout request but returned no transaction id and redirect URL.");
        }

        _metrics.RecordIntentCreated(ProviderKey, request.Purpose.ToString());

        var intent = new ProviderPaymentIntent(
            ProviderIntentId: transactionId,
            Status: PaymentProviderStatus.RequiresAction,
            Amount: request.Amount,
            CheckoutUrl: new Uri(redirectUrl, UriKind.Absolute),
            ClientSecret: null,
            ExpiresAt: request.ExpiresAt,
            FailureCode: null,
            FailureMessage: null);

        _byIdempotencyKey[request.IdempotencyKey] = intent;

        _logger.LogInformation(
            "OnePay payment intent created. intentId={IntentId} organizationId={OrganizationId} "
            + "provider={Provider} providerIntentId={ProviderIntentId} amountMinor={AmountMinor} "
            + "currency={Currency}",
            request.IntentId,
            request.CustomerReference,
            ProviderKey,
            transactionId,
            request.Amount.AmountMinor,
            request.Amount.Currency);

        return intent;
    }

    // ------------------------------------------------------------ reads

    public async Task<ProviderPaymentIntent?> GetPaymentIntentAsync(
        string providerIntentId, CancellationToken cancellationToken = default)
    {
        GuardConfiguration(_options);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerIntentId);

        var read = await SendAsync<TransactionResponse>(
            HttpMethod.Get,
            StatusPath(providerIntentId),
            body: null,
            includeAuthorization: false,
            cancellationToken).ConfigureAwait(false);

        // A 404 is the provider saying it does not know the id; that is the SPI's "return null", not
        // an error. Any other non-success already threw a transport failure from SendAsync.
        if (read is null)
        {
            return null;
        }

        return ToIntent(providerIntentId, read.Data);
    }

    // ------------------------------------------------------------ cancellation

    /// <summary>
    /// OnePay publishes no transaction-cancel operation (there is no cancel path in the API
    /// reference), so the adapter cannot void an intent. A read first makes the refusal specific: a
    /// settled charge is a state failure (409), an unsettled one is an unsupported operation (501).
    /// </summary>
    public async Task<ProviderPaymentIntent> CancelPaymentIntentAsync(
        string providerIntentId, string reason, CancellationToken cancellationToken = default)
    {
        var current = await GetPaymentIntentAsync(providerIntentId, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            throw new PaymentIntentStateException(
                $"OnePay does not know transaction '{providerIntentId}', so it cannot be cancelled.");
        }

        if (current.Status is PaymentProviderStatus.Succeeded
            or PaymentProviderStatus.Cancelled
            or PaymentProviderStatus.Expired)
        {
            throw new PaymentIntentStateException(
                $"A {current.Status} OnePay transaction cannot be cancelled; only an unsettled one can.");
        }

        throw new PaymentProviderNotSupportedException(
            "The OnePay API publishes no transaction-cancel operation; an unsettled transaction "
            + "expires on its own or the customer abandons the hosted page.");
    }

    // ------------------------------------------------------------ refunds

    public async Task<ProviderRefund> RefundAsync(
        ProviderRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardConfiguration(_options);

        if (request.Amount.AmountMinor <= 0)
        {
            throw new PaymentIntentStateException(
                "A OnePay refund must be for a positive amount.");
        }

        var current = await GetPaymentIntentAsync(request.ProviderIntentId, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            throw new PaymentIntentStateException(
                $"OnePay does not know transaction '{request.ProviderIntentId}', so nothing can be refunded.");
        }

        if (current.Status != PaymentProviderStatus.Succeeded)
        {
            throw new PaymentIntentStateException(
                $"Only a Succeeded OnePay transaction can be refunded; "
                + $"'{request.ProviderIntentId}' is {current.Status}.");
        }

        if (request.Amount.AmountMinor > current.Amount.AmountMinor)
        {
            throw new PaymentIntentStateException(
                $"The refund of {request.Amount.AmountMinor} minor units is larger than the "
                + $"{current.Amount.AmountMinor} minor units settled on '{request.ProviderIntentId}'.");
        }

        var isPartial = request.Amount.AmountMinor != current.Amount.AmountMinor;

        if (isPartial && !Capabilities.SupportsPartialRefunds)
        {
            throw new PaymentProviderNotSupportedException(
                "This provider does not support partial refunds.");
        }

        var body = new RefundRequest(
            AppId: _options.AppId,
            OnepayTransactionId: request.ProviderIntentId,
            RefundReason: ReasonCode(request.Reason),
            IsPartially: isPartial,
            Amount: isPartial ? FormatAmount(request.Amount.AmountMinor) : null,
            RefundNote: Note(request));

        var response = await SendAsync<RefundResponse>(
            HttpMethod.Post,
            RefundPath(),
            body,
            includeAuthorization: true,
            cancellationToken).ConfigureAwait(false);

        var refundId = response?.Data?.RefundId;

        var refund = new ProviderRefund(
            ProviderRefundId: string.IsNullOrWhiteSpace(refundId)
                // No id in the response is not a reason to invent a success; the refund is still
                // pending at OnePay (`refund-initiated`), which is what the status below says.
                ? $"onepay_rf_{request.ProviderIntentId}"
                : refundId,
            ProviderIntentId: request.ProviderIntentId,
            Amount: request.Amount,
            Status: MapRefundStatus(response?.Data?.Status),
            CreatedAt: _clock.GetUtcNow().UtcDateTime);

        _metrics.RecordRefund(ProviderKey, refund.Status == PaymentProviderStatus.Succeeded
            ? "succeeded"
            : "processing");

        _logger.LogInformation(
            "OnePay refund submitted. providerIntentId={ProviderIntentId} refundId={RefundId} "
            + "amountMinor={AmountMinor} status={Status}",
            request.ProviderIntentId,
            refund.ProviderRefundId,
            request.Amount.AmountMinor,
            refund.Status);

        return refund;
    }

    // ------------------------------------------------------------ webhooks

    /// <summary>
    /// OnePay signs no callback and publishes no signature or timestamp header, so there is no
    /// signature this adapter could verify. The published integration verifies a completion by
    /// reading the transaction status, which <see cref="GetPaymentIntentAsync"/> does; refusing here
    /// is the honest answer, and the webhook route answers 501 rather than accepting an
    /// unauthenticated body.
    /// </summary>
    public PaymentWebhookEvent VerifyAndParseWebhook(PaymentWebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _metrics.RecordWebhookVerificationFailure(ProviderKey, "unsigned-callback");

        throw new PaymentProviderNotSupportedException(
            "OnePay signs no callback, so there is no signature to verify. Verify a completion by "
            + "reading the transaction status (GetPaymentIntentAsync) instead.");
    }

    // ------------------------------------------------------------ subscriptions

    /// <summary>
    /// OnePay has no provider-side subscription object. Its Card-on-File surface stores a card token
    /// and charges it on demand, leaving the billing period, the proration rule and the cancellation
    /// decision in Aveline's hands - which is exactly the plan §8.5 case: renewal becomes a fresh
    /// intent against <see cref="PaymentPurpose.SubscriptionRenewal"/>.
    /// </summary>
    public Task<ProviderSubscription> CreateOrUpdateSubscriptionAsync(
        CreateProviderSubscriptionRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentProviderNotSupportedException(
            "OnePay publishes no recurring-subscription object. Create a fresh payment intent for "
            + "each billing period instead (plan §8.5).");

    public Task<ProviderSubscription> CancelSubscriptionAsync(
        string providerSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken = default) =>
        throw new PaymentProviderNotSupportedException(
            "OnePay publishes no recurring-subscription object, so there is nothing to cancel at "
            + "OnePay; cancellation is an Aveline subscription-state change (plan §8.5).");

    // ------------------------------------------------------------ status mapping

    /// <summary>
    /// Provider status to Aveline status, exhaustively: every row of <see cref="Statuses"/>, and the
    /// fallback for everything else.
    /// </summary>
    /// <remarks>
    /// The fallback is <see cref="PaymentProviderStatus.Processing"/>, never
    /// <see cref="PaymentProviderStatus.Succeeded"/>: a status this adapter does not recognise is an
    /// unknown outcome, and treating an unknown outcome as settled would grant Blossoms nobody paid
    /// for. The same rule is at the heart of the plan's §8.3 note.
    /// </remarks>
    internal static PaymentProviderStatus MapStatus(string? providerStatus)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus)
            && Statuses.TryGetValue(providerStatus.Trim(), out var mapped))
        {
            return mapped;
        }

        return PaymentProviderStatus.Processing;
    }

    /// <summary>
    /// The refund response's status. Only an explicit success or failure is treated as one; anything
    /// else is <see cref="PaymentProviderStatus.Processing"/>, because OnePay's documented refund
    /// acknowledgement is <c>refund-initiated</c>, which is not settlement.
    /// </summary>
    internal static PaymentProviderStatus MapRefundStatus(string? refundStatus) => refundStatus switch
    {
        null => PaymentProviderStatus.Processing,
        var value when value.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase) => PaymentProviderStatus.Succeeded,
        var value when value.Equals("SUCCESSFUL", StringComparison.OrdinalIgnoreCase) => PaymentProviderStatus.Succeeded,
        var value when value.Equals("1", StringComparison.Ordinal) => PaymentProviderStatus.Succeeded,
        var value when value.Equals("REFUNDED", StringComparison.OrdinalIgnoreCase) => PaymentProviderStatus.Succeeded,
        var value when value.Equals("FAILED", StringComparison.OrdinalIgnoreCase) => PaymentProviderStatus.Failed,
        var value when value.Equals("FAILURE", StringComparison.OrdinalIgnoreCase) => PaymentProviderStatus.Failed,
        var value when value.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) => PaymentProviderStatus.Failed,
        _ => PaymentProviderStatus.Processing,
    };

    // ------------------------------------------------------------ the request shape

    /// <summary>
    /// <c>SHA256(app_id + currency + amount + HASH_SALT)</c>, lower-case hex, over the same
    /// two-decimal amount string that goes in the body. The salt is a parameter rather than a field
    /// so the signing rule is testable without a configured secret.
    /// </summary>
    internal static string Sign(string appId, string currency, string amount, string hashSalt)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(hashSalt);

        var concatenated = appId + currency + amount + hashSalt;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(concatenated));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Minor units to OnePay's two-decimal amount string. <see cref="Money"/> is already minor units,
    /// so this is formatting only - the same rule as <see cref="Money.ToMajorUnits"/>, written on the
    /// wire in the format OnePay documents (<c>100.00</c>).
    /// </summary>
    internal static string FormatAmount(long amountMinor) =>
        (amountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A free-text reason mapped onto OnePay's closed reason-code vocabulary.</summary>
    internal static string ReasonCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return DefaultRefundReason;
        }

        if (reason.Contains("fraud", StringComparison.OrdinalIgnoreCase))
        {
            return "FRAUDULENT";
        }

        if (reason.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return "DUPLICATED";
        }

        if (reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("out of order", StringComparison.OrdinalIgnoreCase))
        {
            return "OUT_OF_ORDER";
        }

        return DefaultRefundReason;
    }

    // ------------------------------------------------------------ internals

    private static void GuardAmount(CreateProviderIntentRequest request)
    {
        if (request.Amount.AmountMinor <= 0)
        {
            throw new PaymentProviderNotSupportedException(
                "The OnePay adapter cannot create a charge for a non-positive amount.");
        }
    }

    private string Note(ProviderRefundRequest request) =>
        // OnePay has no idempotency key on the refund endpoint, so the key travels in the free-text
        // note: it makes a duplicate submission traceable in the merchant dashboard even though the
        // provider cannot deduplicate it for us.
        $"Aveline refund key {request.IdempotencyKey}: {request.Reason}";

    private string CheckoutPath() => Normalise(_options.CheckoutPath, DefaultCheckoutPath);

    private string RefundPath() => Normalise(_options.RefundPath, DefaultRefundPath);

    private string StatusPath(string providerIntentId)
    {
        var template = Normalise(_options.StatusPath, DefaultStatusPath);
        var escaped = Uri.EscapeDataString(providerIntentId);

        return template.Contains("{id}", StringComparison.Ordinal)
            ? template.Replace("{id}", escaped, StringComparison.Ordinal)
            : $"{template.TrimEnd('/')}/{escaped}";
    }

    private static string Normalise(string? configured, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return value.StartsWith('/') ? value : "/" + value;
    }

    private static string? AsString(JsonElement? element) =>
        element is not { } value || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();

    private static Money? AsMoney(
        JsonElement? amountElement, JsonElement? currencyElement, string fallbackCurrency)
    {
        if (amountElement is not { } value
            || value.ValueKind != JsonValueKind.String
            || !decimal.TryParse(
                value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var major))
        {
            return null;
        }

        var minor = (long)decimal.Round(major * 100m, 0, MidpointRounding.AwayFromZero);
        var currency = AsString(currencyElement);
        return new Money(minor, string.IsNullOrWhiteSpace(currency)
            ? fallbackCurrency.ToUpperInvariant()
            : currency.ToUpperInvariant());
    }

    private ProviderPaymentIntent ToIntent(string providerIntentId, TransactionData? data)
    {
        var status = MapStatus(AsString(data?.Status));

        var amount = AsMoney(data?.Amount, data?.Currency, _defaultCurrency)
            // A read that does not carry an amount still has a status the caller asked for; the
            // intent is reported with a zero amount rather than a fabricated one.
            ?? new Money(0, _defaultCurrency);

        // A failure's own message is the only useful thing to carry upward; a non-failure's message
        // ("PENDING") is not a failure reason and must not look like one.
        var failureMessage = status == PaymentProviderStatus.Failed
            ? data?.StatusMessage ?? AsString(data?.Status)
            : null;

        var transactionId = string.IsNullOrWhiteSpace(data?.TransactionId)
            ? providerIntentId
            : data!.TransactionId!;

        return new ProviderPaymentIntent(
            ProviderIntentId: transactionId,
            Status: status,
            Amount: amount,
            CheckoutUrl: null,
            ClientSecret: null,
            ExpiresAt: null,
            FailureCode: status == PaymentProviderStatus.Failed ? "declined" : null,
            FailureMessage: failureMessage);
    }

    /// <summary>
    /// Sends a request and deserialises OnePay's <c>{ status, data }</c> envelope. Returns null when
    /// OnePay answered <c>404</c> (the caller turns that into "unknown id"); every other non-success
    /// and every transport fault becomes a <see cref="PaymentProviderTransportException"/>, so the
    /// endpoint layer's 502 mapping and the plan §6.6 rule ("an unknown outcome is not a failure")
    /// both hold.
    /// </summary>
    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method, string path, object? body, bool includeAuthorization, CancellationToken cancellationToken)
        where TResponse : class
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var message = new HttpRequestMessage(method, path);

        if (includeAuthorization)
        {
            message.Headers.TryAddWithoutValidation("Authorization", _options.AppToken);
        }

        if (body is not null)
        {
            message.Content = new StringContent(
                JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }

        _logger.LogDebug(
            "OnePay request. provider={Provider} method={Method} path={Path}",
            ProviderKey,
            method.Method,
            path);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            _metrics.RecordProviderError(ProviderKey, "http", "transport");
            throw new PaymentProviderTransportException(
                $"The OnePay provider could not be reached for {method.Method} {path}.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _metrics.RecordProviderError(
                    ProviderKey, "http", ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));

                throw new PaymentProviderTransportException(
                    $"OnePay answered {(int)response.StatusCode} for {method.Method} {path}.");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return JsonSerializer.Deserialize<TResponse>(payload, Json);
            }
            catch (JsonException exception)
            {
                _metrics.RecordProviderError(ProviderKey, "http", "malformed_response");
                throw new PaymentProviderTransportException(
                    $"OnePay returned a body that is not the documented envelope for "
                    + $"{method.Method} {path}.",
                    exception);
            }
        }
    }

    // ------------------------------------------------------------ wire shapes

    /// <summary>
    /// The create-transaction body. Field names are OnePay's own, and every one is in the published
    /// request table; none is card data.
    /// </summary>
    private sealed record CreateTransactionRequest(
        [property: JsonPropertyName("app_id")] string AppId,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("hash")] string Hash,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("customer_first_name")] string CustomerFirstName,
        [property: JsonPropertyName("customer_last_name")] string CustomerLastName,
        [property: JsonPropertyName("customer_phone_number")] string CustomerPhoneNumber,
        [property: JsonPropertyName("customer_email")] string CustomerEmail,
        [property: JsonPropertyName("transaction_redirect_url")] string TransactionRedirectUrl,
        // The Aveline trace. OnePay's field is additionalData; the CLR name is Metadata so it cannot
        // collide with the interface indexer, and the wire name is the documented one.
        [property: JsonPropertyName("additionalData")] string Metadata);

    private sealed record RefundRequest(
        [property: JsonPropertyName("app_id")] string AppId,
        [property: JsonPropertyName("onepay_transaction_id")] string OnepayTransactionId,
        [property: JsonPropertyName("refund_reason")] string RefundReason,
        [property: JsonPropertyName("is_partially")] bool IsPartially,
        [property: JsonPropertyName("amount")] string? Amount,
        [property: JsonPropertyName("refund_note")] string RefundNote);

    private sealed record AvelineMetadata(
        [property: JsonPropertyName("aveline_intent_id")] string AvelineIntentId,
        [property: JsonPropertyName("aveline_idempotency_key")] string AvelineIdempotencyKey,
        [property: JsonPropertyName("aveline_purpose")] string AvelinePurpose,
        [property: JsonPropertyName("aveline_customer_reference")] string AvelineCustomerReference);

    /// <summary>
    /// The response envelope. <c>status</c> is read as a <see cref="JsonElement"/> because the
    /// published callback sample sends it as a number while other published responses send it as a
    /// string; the element accepts both without a converter.
    /// </summary>
    private sealed record CreateTransactionResponse(
        [property: JsonPropertyName("status")] JsonElement? Status,
        [property: JsonPropertyName("data")] CreateTransactionData? Data);

    private sealed record CreateTransactionData(
        [property: JsonPropertyName("transaction_id")] string? TransactionId,
        [property: JsonPropertyName("redirect_url")] string? RedirectUrl);

    private sealed record TransactionResponse(
        [property: JsonPropertyName("status")] JsonElement? Status,
        [property: JsonPropertyName("data")] TransactionData? Data);

    private sealed record TransactionData(
        [property: JsonPropertyName("transaction_id")] string? TransactionId,
        [property: JsonPropertyName("status")] JsonElement? Status,
        [property: JsonPropertyName("status_message")] string? StatusMessage,
        [property: JsonPropertyName("amount")] JsonElement? Amount,
        [property: JsonPropertyName("currency")] JsonElement? Currency);

    private sealed record RefundResponse(
        [property: JsonPropertyName("status")] JsonElement? Status,
        [property: JsonPropertyName("data")] RefundData? Data);

    private sealed record RefundData(
        [property: JsonPropertyName("refund_id")] string? RefundId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("amount")] string? Amount);
}
