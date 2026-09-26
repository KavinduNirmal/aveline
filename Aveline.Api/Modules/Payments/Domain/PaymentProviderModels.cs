namespace Aveline.Api.Modules.Payments.Domain;

/// <summary>
/// Money at the provider boundary: integer minor units plus an ISO-4217 code (decision D3,
/// plan §6.7). Every real provider API takes minor units, while the repository stores
/// <c>decimal(18,2)</c> LKR, so <see cref="Lkr"/> is the single conversion site.
/// </summary>
/// <param name="AmountMinor">The amount in the currency's minor unit (cents for LKR).</param>
/// <param name="Currency">The ISO-4217 code, for example <c>LKR</c>.</param>
public readonly record struct Money(long AmountMinor, string Currency)
{
    /// <summary>
    /// Converts an LKR amount in major units to minor units, rounding a half-cent away from zero.
    /// The rounding mode matches <c>BlossomCalculator</c> rather than the CLR's default banker's
    /// rounding, so a half-cent cannot round down here and up there.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The amount is zero or negative, which cannot
    /// be a charge.</exception>
    public static Money Lkr(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A charge amount must be greater than zero.");
        }

        return new Money((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero), "LKR");
    }

    /// <summary>The amount in major units, for the domain's <c>decimal(18,2)</c> columns.</summary>
    public decimal ToMajorUnits() => AmountMinor / 100m;
}

/// <summary>What the charge is for. Drives the settlement effect, never the provider call.</summary>
public enum PaymentPurpose
{
    BlossomTopUp,
    SubscriptionInitial,
    SubscriptionRenewal,
    SubscriptionProration,
    CommerceOrder,
}

/// <summary>
/// The provider-agnostic create request. <paramref name="IntentId"/> is Aveline's id and is sent as
/// the provider's idempotency key and metadata so an orphaned provider charge is traceable back.
/// </summary>
public sealed record CreateProviderIntentRequest(
    Guid IntentId,
    Money Amount,
    PaymentPurpose Purpose,
    string Description,
    string CustomerReference,
    string IdempotencyKey,
    Uri? ReturnUrl,
    DateTime? ExpiresAt);

/// <summary>Current provider-side state of a charge.</summary>
public enum PaymentProviderStatus
{
    /// <summary>The customer must complete something (redirect, authorisation, transfer).</summary>
    RequiresAction,

    /// <summary>Accepted, settlement pending.</summary>
    Processing,

    Succeeded,
    Failed,
    Cancelled,
    Expired,
}

/// <summary>What one adapter can actually do. Callers must consult this rather than assume.</summary>
public sealed record PaymentProviderCapabilities(
    bool SupportsRecurringSubscriptions,
    bool SupportsProration,
    bool SupportsPartialRefunds,
    bool SupportsCancelAtPeriodEnd,
    bool SupportsHostedCheckout,
    bool SettlesAsynchronously);

/// <summary>The provider's view of a charge. No provider SDK type appears here.</summary>
public sealed record ProviderPaymentIntent(
    string ProviderIntentId,
    PaymentProviderStatus Status,
    Money Amount,
    Uri? CheckoutUrl,
    string? ClientSecret,
    DateTime? ExpiresAt,
    string? FailureCode,
    string? FailureMessage);

public sealed record ProviderRefundRequest(
    string ProviderIntentId,
    Money Amount,
    string IdempotencyKey,
    string Reason);

public sealed record ProviderRefund(
    string ProviderRefundId,
    string ProviderIntentId,
    Money Amount,
    PaymentProviderStatus Status,
    DateTime CreatedAt);

/// <summary>An inbound webhook exactly as it arrived, before any JSON binding.</summary>
public sealed record PaymentWebhookRequest(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    string? RemoteIp,
    DateTimeOffset ReceivedAt);

public sealed record PaymentWebhookEvent(
    string ProviderEventId,
    PaymentWebhookEventType Type,
    string? ProviderIntentId,
    string? ProviderRefundId,
    Money? Amount,
    string? FailureCode,
    DateTimeOffset OccurredAt,
    string RawPayload);

public enum PaymentWebhookEventType
{
    IntentSucceeded,
    IntentFailed,
    IntentCancelled,
    IntentExpired,
    RefundSucceeded,
    RefundFailed,

    /// <summary>
    /// The customer's bank has opened a dispute (a chargeback) against a settled charge. It is an
    /// explicit type rather than <see cref="Unknown"/> because its effect is known: an append-only
    /// reversal is recorded through the income ledger (plan §9.6, P10).
    /// </summary>
    DisputeOpened,

    /// <summary>
    /// A type no handler acts on. The settlement path stores it **unprocessed** and logs it, which
    /// is the deliberate fail-safe (plan §9.6).
    /// </summary>
    Unknown,
}

/// <summary>
/// The provider's recurring agreement. <paramref name="ProviderSubscriptionId"/> is null for a
/// create; the adapter returns the assigned id.
/// </summary>
public sealed record CreateProviderSubscriptionRequest(
    string? ProviderSubscriptionId,
    string ProviderCustomerId,
    Money RecurringAmount,
    string Interval,
    string IdempotencyKey,
    IReadOnlyList<string>? AllowedPaymentMethodTypes);

public sealed record ProviderSubscription(
    string ProviderSubscriptionId,
    string ProviderCustomerId,
    PaymentProviderStatus Status,
    Money RecurringAmount,
    string Interval,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);
