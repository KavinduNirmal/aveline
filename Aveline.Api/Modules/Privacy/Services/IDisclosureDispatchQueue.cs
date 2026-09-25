namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The enqueue half of the disclosure queue. The webhook awaits this, and in production it completes
/// as soon as the intent is in the channel - it never waits on Meta, which is what keeps the webhook
/// fast and stops a slow provider from triggering Meta's own webhook retry (plan §4.4, §15 Q-1).
/// </summary>
public interface IDisclosureDispatchQueue
{
    /// <summary>
    /// Queues one intent. Returns <c>false</c> when the queue is full, in which case nothing was
    /// queued and the caller should log it: the next inbound message retries because the consent row
    /// was never stamped.
    /// </summary>
    ValueTask<bool> EnqueueAsync(
        DisclosureIntent intent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues one opt-out acknowledgement (plan §11 item 4.5). Returns <c>false</c> when the queue is
    /// full; the acknowledgement gate already owns the 24-hour window, so a refused enqueue loses the
    /// confirmation rather than duplicating it, and the caller logs the refusal.
    /// </summary>
    ValueTask<bool> EnqueueAcknowledgementAsync(
        OptOutAcknowledgementIntent intent, CancellationToken cancellationToken = default);
}

/// <summary>
/// The drain half, addressed separately so the worker cannot accidentally depend on the enqueue
/// surface (or vice versa) and so a test can replace one without the other.
/// </summary>
public interface IDisclosureDispatchQueueReader
{
    /// <summary>Yields intents as they arrive, until <paramref name="cancellationToken"/> is signalled.</summary>
    IAsyncEnumerable<DisclosureIntent> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>Yields opt-out acknowledgement intents as they arrive.</summary>
    IAsyncEnumerable<OptOutAcknowledgementIntent> ReadAcknowledgementsAsync(
        CancellationToken cancellationToken);
}
