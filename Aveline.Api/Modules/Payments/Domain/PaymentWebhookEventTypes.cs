namespace Aveline.Api.Modules.Payments.Domain;

/// <summary>
/// The one place a provider's wire event name becomes a <see cref="PaymentWebhookEventType"/>.
/// </summary>
/// <remarks>
/// <para>
/// Before P10 every adapter carried its own private string-to-type switch, so a type could be known to
/// one adapter and unknown to the next, and there was no way to describe a dispute without adding it
/// everywhere. Adapters now call <see cref="Parse"/>; the branch that decides "we do not understand
/// this" stays a single, auditable default.
/// </para>
/// <para>
/// Matching is ordinal and exact: a provider's vocabulary is a contract, and a lenient match is how
/// two event names that merely look alike get silently conflated. An unrecognised name is
/// <see cref="PaymentWebhookEventType.Unknown"/>, which the settlement path stores unprocessed and
/// logs rather than guessing at (plan §9.6).
/// </para>
/// </remarks>
public static class PaymentWebhookEventTypes
{
    /// <summary>
    /// Maps a provider wire name to its Aveline type, or <see cref="PaymentWebhookEventType.Unknown"/>
    /// when nothing recognises it.
    /// </summary>
    public static PaymentWebhookEventType Parse(string? type) => type switch
    {
        "intent.succeeded" => PaymentWebhookEventType.IntentSucceeded,
        "intent.failed" => PaymentWebhookEventType.IntentFailed,
        "intent.cancelled" => PaymentWebhookEventType.IntentCancelled,
        "intent.expired" => PaymentWebhookEventType.IntentExpired,
        "refund.succeeded" => PaymentWebhookEventType.RefundSucceeded,
        "refund.failed" => PaymentWebhookEventType.RefundFailed,

        // The mock's name, plus the two spellings the external adapters use for the same fact. A
        // dispute is "money the bank took back", so it is one concept with several wire names.
        "dispute.opened" or "charge.disputed" or "charge.dispute.created" =>
            PaymentWebhookEventType.DisputeOpened,

        _ => PaymentWebhookEventType.Unknown,
    };
}
