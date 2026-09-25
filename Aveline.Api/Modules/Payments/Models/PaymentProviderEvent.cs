using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Payments.Models;

/// <summary>
/// The webhook inbox (plan §6.3). The inbox is not an optimisation: a provider retries webhooks, and
/// the unique <c>(Provider, ProviderEventId)</c> index is what distinguishes a duplicate from a
/// retry of a failed dispatch, which an idempotency-only design cannot tell apart.
/// </summary>
/// <remarks>
/// Null <see cref="ProcessedAt"/> means unprocessed, which is what makes a failed dispatch retryable.
/// Deliberately carries no card, PAN, CVC, expiry, or token field (constraint C11).
/// </remarks>
public sealed class PaymentProviderEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's event id; the replay guard, unique with <see cref="Provider"/>.</summary>
    public string ProviderEventId { get; set; } = string.Empty;

    public PaymentWebhookEventType EventType { get; set; } = PaymentWebhookEventType.Unknown;

    public string? ProviderIntentId { get; set; }

    /// <summary>Nullable; a check constraint refuses a negative value.</summary>
    public long? AmountMinor { get; set; }

    public string? Currency { get; set; }

    /// <summary>Provider-reported time.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Server time.</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The raw payload, kept as <c>jsonb</c> for forensics.</summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary>Null means unprocessed.</summary>
    public DateTime? ProcessedAt { get; set; }

    public string? ProcessingError { get; set; }
}
