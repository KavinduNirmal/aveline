using Microsoft.Extensions.Hosting;

namespace Aveline.Api.Modules.Payments;

/// <summary>
/// Configuration for the payment module (plan §6.5). Bound from the <c>Payments</c> section.
/// </summary>
/// <remarks>
/// Every default is safe on its own: this repository has no <c>Payments</c> section in any
/// <c>appsettings</c> file, so an unconfigured deployment silently takes these values. That is why
/// <see cref="Provider"/> defaults to <c>"manual"</c> — the honest representation of what the
/// system does today (an operator confirms receipt) — rather than to a provider that could invent
/// settled money.
/// </remarks>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// The key of the adapter that cannot settle anything by itself: an operator confirms receipt
    /// out of band. <c>ManualPaymentProvider.ProviderKey</c> carries the same value for the adapter.
    /// </summary>
    public const string ManualProviderKey = "manual";

    /// <summary>
    /// The key of the **external** adapter (plan Phase 8, decision Q5): OnePay, the Sri Lankan
    /// gateway whose hosted redirection page keeps every card detail out of Aveline (A2).
    /// <c>OnePayPaymentProvider.ProviderKey</c> carries the same value for the adapter.
    /// </summary>
    public const string OnePayProviderKey = "onepay";

    /// <summary>
    /// <c>"manual" | "mock" | "onepay" | "stripe"</c>. Defaults to <c>"manual"</c> so an unconfigured
    /// deployment cannot accidentally invent settled money.
    /// </summary>
    public string Provider { get; set; } = "manual";

    /// <summary>
    /// True when the configured provider is a client that can itself settle money, which is what
    /// the revenue read's <c>revenueProviderSettlementAvailable</c> reports (plan §9.4's read
    /// surface). <c>manual</c> is deliberately the one key that does not qualify: nothing settles
    /// automatically, so no figure is collected money until an operator confirms it.
    /// </summary>
    public bool ProviderSettlesMoney => SettlesMoney(Provider);

    /// <summary>The same rule applied to a key, for callers that do not hold the options.</summary>
    public static bool SettlesMoney(string? providerKey) =>
        !string.Equals(providerKey, ManualProviderKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>The currency every charge is created in; the settlement guard refuses a mismatch.</summary>
    public string Currency { get; set; } = "LKR";

    /// <summary>
    /// How many days after settlement a refund may still be issued, or <c>null</c> (the default) for
    /// no automatic window, in which case the operator decides (plan §9.6).
    /// </summary>
    /// <remarks>
    /// <b>The policy is deliberately unresolved.</b> <c>docs/architecture/pricing_plan.md</c> proposes
    /// a seven-day full-refund window and says in the same breath that the final policy must be
    /// reviewed against payment-provider and consumer-protection requirements. Hardcoding seven days
    /// here would decide that review by accident, so the value is configuration and the safe default
    /// is to leave the decision with the operator. A configured value refuses a refund struck outside
    /// it with <c>409 payment-intent-state</c>.
    /// </remarks>
    public int? RefundWindowDays { get; set; }

    public MockProviderOptions Mock { get; set; } = new();

    public StripeProviderOptions Stripe { get; set; } = new();

    public OnePayProviderOptions OnePay { get; set; } = new();

    public PaymentWebhookOptions Webhook { get; set; } = new();

    /// <summary>
    /// The Commerce order checkout's one-release rollback switch (plan §8.4 S8, §9.7). Bound from
    /// <c>Payments:Commerce</c>.
    /// </summary>
    public CommercePaymentOptions Commerce { get; set; } = new();

    /// <summary>
    /// Validates the options against the running environment, so a mis-configured provider fails
    /// startup rather than the first request (the <c>AnalyticsModule</c> precedent).
    /// </summary>
    /// <remarks>
    /// The mock guard keys off <see cref="IHostEnvironment"/> and not off a config value, because
    /// <c>Payments:*</c> defaults silently apply in every environment. A deployer who forgets to
    /// set <see cref="Provider"/> gets <c>manual</c>; a deployer who sets it to <c>mock</c> outside
    /// Development gets a startup failure rather than a working fake gateway.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The configuration is unsafe for this environment.</exception>
    public void Validate(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (string.Equals(Provider, "mock", StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment() && !Mock.AllowInNonDevelopment)
            {
                throw new InvalidOperationException(
                    "Payments:Provider is 'mock' outside the Development environment. " +
                    "The mock provider must not collect money in a deployed environment; set " +
                    "Payments:Provider to 'manual' or 'stripe', or set " +
                    "Payments:Mock:AllowInNonDevelopment to true deliberately.");
            }

            if (!Mock.Enabled)
            {
                throw new InvalidOperationException(
                    "Payments:Mock:Enabled must be true to use the mock provider.");
            }

            if (string.IsNullOrWhiteSpace(Mock.WebhookSigningSecret))
            {
                throw new InvalidOperationException(
                    "Payments:Mock:WebhookSigningSecret must be set to use the mock provider.");
            }
        }

        if (string.Equals(Provider, "stripe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Stripe.SecretKey))
            {
                throw new InvalidOperationException("Payments:Stripe:SecretKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(Stripe.WebhookSigningSecret))
            {
                throw new InvalidOperationException(
                    "Payments:Stripe:WebhookSigningSecret is not configured.");
            }
        }

        if (string.Equals(Provider, OnePayProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            // Fail at startup, not at the first checkout (plan Phase 8). AppId and HashSalt are the
            // whole required set: without them no request can be signed, so the deployment could not
            // take a payment at all. The customer defaults are only needed by the redirection API's
            // request shape, and the adapter name-checks them when a create actually needs them.
            if (string.IsNullOrWhiteSpace(OnePay.AppId))
            {
                throw new InvalidOperationException(
                    "Payments:OnePay:AppId is not configured. Set the App ID from the OnePay "
                    + "merchant dashboard.");
            }

            if (string.IsNullOrWhiteSpace(OnePay.HashSalt))
            {
                throw new InvalidOperationException(
                    "Payments:OnePay:HashSalt is not configured. The hash salt signs every OnePay "
                    + "request and must never be committed or sent to a client.");
            }

            if (!string.IsNullOrWhiteSpace(OnePay.BaseUrl)
                && !Uri.TryCreate(OnePay.BaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    "Payments:OnePay:BaseUrl must be an absolute URL, for example "
                    + "https://api.onepay.lk.");
            }
        }
    }
}

/// <summary>Configuration for the Development-only mock adapter (behaviours are specified in §7).</summary>
public sealed class MockProviderOptions
{
    /// <summary>False everywhere by default; the factory refuses to resolve the mock when false.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The only way to run the mock outside Development. Must be set deliberately, with a written
    /// reason, because it defeats the primary anti-production guard.
    /// </summary>
    public bool AllowInNonDevelopment { get; set; }

    /// <summary>When true the mock settles on creation, so a demo needs no webhook.</summary>
    public bool AutoSettle { get; set; }

    /// <summary>Base URL for the mock's Development-only checkout page.</summary>
    public string CheckoutBaseUrl { get; set; } = "/api/v1/dev/mock-checkout";

    /// <summary>Signs mock webhooks; distinct from any real provider secret.</summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>Fails every created intent with this code, for demonstrating the failure path.</summary>
    public string? ForceFailureCode { get; set; }
}

/// <summary>Webhook ingestion settings shared by every provider.</summary>
public sealed class PaymentWebhookOptions
{
    /// <summary>Replay guard, matching the Clerk webhook convention. Default 5 minutes.</summary>
    public int ToleranceSeconds { get; set; } = 300;

    /// <summary>Optional allowlist, mirroring <c>Webhook:AllowedIps</c> for WhatsApp.</summary>
    public List<string> AllowedIps { get; set; } = [];
}

/// <summary>
/// The Commerce order checkout's rollback switch (plan §8.4 S8's final row, Phase 9). Bound from
/// <c>Payments:Commerce</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One release, then deleted.</b> Phase 9 replaced a fabricated checkout URL with a
/// provider-neutral <c>CommerceOrder</c> intent and replaced a caller-trusted confirmation with a
/// poll of that intent. Both changes alter shipped behaviour, so §8.4 S8 keeps the old code path
/// behind a single switch for one release. Setting this to <c>false</c> restores the exact
/// pre-Phase-9 path: the operator-supplied URL literal and the confirmation that believes the
/// caller's <c>GatewayTransactionId</c>. Delete the branch and this property together after the
/// release that ships Phase 9.
/// </para>
/// <para>
/// <b>The default is the new path.</b> A deployment that configures nothing must not keep taking
/// payments against a URL nothing can settle; the rollback is a deliberate act with a written
/// reason, not a default.
/// </para>
/// </remarks>
public sealed class CommercePaymentOptions
{
    /// <summary>
    /// True (the default) creates a <c>CommerceOrder</c> payment intent through
    /// <c>IPaymentIntentService</c> and reads settlement from the provider. False restores the
    /// pre-Phase-9 path for one release.
    /// </summary>
    public bool UseProviderIntents { get; set; } = true;
}

/// <summary>Configuration for the external Stripe adapter (implemented in Phase 8).</summary>
public sealed class StripeProviderOptions
{
    public string SecretKey { get; set; } = string.Empty;

    public string PublishableKey { get; set; } = string.Empty;

    public string WebhookSigningSecret { get; set; } = string.Empty;

    public string Mode { get; set; } = "test";
}

/// <summary>
/// Configuration for the external OnePay adapter (plan Phase 8, decision Q5). Bound from
/// <c>Payments:OnePay</c> and supplied as environment variables in every environment.
/// </summary>
/// <remarks>
/// <para>
/// <b>The credentials.</b> <see cref="AppId"/> identifies the merchant account;
/// <see cref="HashSalt"/> signs every request via
/// <c>SHA256(app_id + currency + amount + hash_salt)</c>; <see cref="AppToken"/> is the
/// <c>Authorization</c> header OnePay's refund and Card-on-File endpoints require. The salt and the
/// token are secrets: they are read from configuration only, never logged, and never sent to a
/// client.
/// </para>
/// <para>
/// <b>Paths are configuration, not constants.</b> The docs that could be read this phase publish the
/// checkout path explicitly; the status and refund paths are named in prose ("Get Transaction API",
/// <c>/v3/transaction/status/</c>) rather than in a machine-readable reference. Making each path a
/// setting means a documented endpoint change is a config change, and a sandbox probe can confirm the
/// real value without a code change.
/// </para>
/// </remarks>
public sealed class OnePayProviderOptions
{
    /// <summary>The <c>app_id</c> from the OnePay merchant dashboard.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>The <c>HASH_SALT</c>; a secret. Required when <c>Payments:Provider</c> is <c>onepay</c>.</summary>
    public string HashSalt { get; set; } = string.Empty;

    /// <summary>The App Token for the refund endpoint's <c>Authorization</c> header; a secret.</summary>
    public string AppToken { get; set; } = string.Empty;

    /// <summary>OnePay's API origin. Defaults to the published live base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.onepay.lk";

    /// <summary>The create-transaction path (Redirection API).</summary>
    public string CheckoutPath { get; set; } = "/v3/checkout/link/";

    /// <summary>
    /// The transaction-status path, with <c>{id}</c> replaced by the escaped transaction id.
    /// </summary>
    public string StatusPath { get; set; } = "/v3/transaction/status/{id}";

    /// <summary>The refund path.</summary>
    public string RefundPath { get; set; } = "/v3/transaction/refund/";

    /// <summary>
    /// What the redirection request's required customer fields carry when the caller has no customer
    /// profile to hand. The SPI carries an organisation reference, not a person, so these are a
    /// deployment-level answer to OnePay's request shape - the same shape the OnePay-issued hosted
    /// page would otherwise ask the customer for anyway.
    /// </summary>
    public OnePayCustomerDefaults CustomerDefaults { get; set; } = new();
}

/// <summary>The customer fields OnePay's redirection request requires.</summary>
public sealed class OnePayCustomerDefaults
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
