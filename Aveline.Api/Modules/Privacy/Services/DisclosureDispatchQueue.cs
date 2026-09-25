using System.Threading.Channels;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The default in-process disclosure queue: a bounded <see cref="Channel{T}"/>. Bounded, not
/// unbounded, so a provider outage cannot grow the process's memory without limit; a full queue
/// refuses the intent (rather than displacing a queued one) and the caller logs it. Refusal is safe
/// because the disclosure is retried by the next inbound message - the consent row is only stamped
/// once a send is attempted.
/// </summary>
/// <remarks>
/// <b>Durability.</b> The queue is in-process, so a restart loses undrained intents. That is a
/// deliberate Pr3 trade: the durable record is <c>CustomerConsent.DisclosureShownAt IS NULL</c>, and
/// the retry trigger is the customer's own next inbound message. A crash therefore delays the
/// disclosure by one message rather than losing it. A durable outbox is the natural Phase 6
/// upgrade if the product wants a bounded retry window without waiting for the customer.
/// </remarks>
public sealed class DisclosureDispatchQueue : IDisclosureDispatchQueue, IDisclosureDispatchQueueReader
{
    /// <summary>The default bound. Well above any realistic per-instance inbound burst.</summary>
    public const int DefaultCapacity = 1024;

    private readonly Channel<DisclosureIntent> _channel;
    private readonly Channel<OptOutAcknowledgementIntent> _acknowledgements;

    public DisclosureDispatchQueue(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _channel = Channel.CreateBounded<DisclosureIntent>(
            new BoundedChannelOptions(capacity)
            {
                // TryWrite must answer false when full so the caller can observe the refusal; the
                // displacing modes would report success while silently dropping an intent.
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });

        // A second, identically-bounded channel for opt-out acknowledgements. Separate so a backlog
        // of disclosures can never delay the one message a customer who just opted out must receive.
        _acknowledgements = Channel.CreateBounded<OptOutAcknowledgementIntent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <inheritdoc />
    public ValueTask<bool> EnqueueAsync(
        DisclosureIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return ValueTask.FromResult(_channel.Writer.TryWrite(intent));
    }

    /// <inheritdoc />
    public ValueTask<bool> EnqueueAcknowledgementAsync(
        OptOutAcknowledgementIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return ValueTask.FromResult(_acknowledgements.Writer.TryWrite(intent));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<DisclosureIntent> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<OptOutAcknowledgementIntent> ReadAcknowledgementsAsync(
        CancellationToken cancellationToken)
        => _acknowledgements.Reader.ReadAllAsync(cancellationToken);
}
