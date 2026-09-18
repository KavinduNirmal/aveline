using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Redis Pub/Sub implementation of <see cref="IEventBus"/> (ADR-014).
///
/// Publishing writes the serialized envelope to the org-scoped channel
/// <c>aveline:&lt;org_id&gt;:&lt;event_type&gt;</c>. Subscribing registers an in-memory handler for an
/// event type; the actual Redis <c>PSUBSCRIBE</c> connection is owned by
/// <see cref="RedisSubscriptionService"/>, which dispatches received messages back into this
/// bus via <see cref="DispatchAsync"/>.
/// </summary>
public sealed class RedisEventBus : IEventBus
{
    private readonly IConnectionMultiplexer _connection;
    private readonly IEventSerializer _serializer;
    private readonly EventBusMetrics _metrics;
    private readonly ILogger<RedisEventBus> _logger;

    // eventType -> registered handlers. Handlers are invoked in registration order.
    private readonly ConcurrentDictionary<string, List<Func<EventEnvelope, CancellationToken, Task>>> _handlers = new();

    public RedisEventBus(
        IConnectionMultiplexer connection,
        IEventSerializer serializer,
        EventBusMetrics metrics,
        ILogger<RedisEventBus> logger)
    {
        _connection = connection;
        _serializer = serializer;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task PublishAsync(
        string eventType,
        Guid? organizationId,
        object? payload,
        Guid? traceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var envelope = EventEnvelope.Create(eventType, organizationId, payload, traceId);
        var channel = EventChannel.ForOrganization(organizationId ?? Guid.Empty, eventType);
        var json = _serializer.Serialize(envelope);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _connection.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(channel), json)
                .WaitAsync(cancellationToken);
            _metrics.RecordPublished(eventType);
            _logger.LogDebug("Published event {EventType} to channel {Channel} (eventId={EventId}).",
                eventType, channel, envelope.EventId);
        }
        catch (Exception ex)
        {
            _metrics.RecordFailed(eventType);
            _logger.LogError(ex, "Failed to publish event {EventType} to channel {Channel}.", eventType, channel);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordPublishLatency(stopwatch.Elapsed.TotalMilliseconds, eventType);
        }
    }

    public Task SubscribeAsync(
        string eventType,
        Func<EventEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Func<EventEnvelope, CancellationToken, Task>> { handler },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(handler);
                }
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(
        string eventType,
        Func<EventEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            lock (handlers)
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _handlers.TryRemove(eventType, out _);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The event types that currently have at least one registered handler. Used by
    /// <see cref="RedisSubscriptionService"/> to decide which patterns to subscribe to.
    /// </summary>
    public IReadOnlyCollection<string> SubscribedEventTypes => _handlers.Keys.ToArray();

    /// <summary>
    /// Dispatches a raw Redis message to the handlers registered for its event type.
    /// Malformed messages are logged and skipped. A handler failure is logged but never
    /// aborts the remaining handlers or the caller.
    /// </summary>
    public async Task DispatchAsync(string channel, string message, CancellationToken cancellationToken = default)
    {
        var envelope = _serializer.Deserialize(message);
        if (envelope is null)
        {
            _logger.LogWarning("Skipping malformed event message on channel {Channel}.", channel);
            _metrics.RecordFailed("unknown");
            return;
        }

        _metrics.RecordReceived(envelope.EventType);

        if (!_handlers.TryGetValue(envelope.EventType, out var handlers))
        {
            _logger.LogDebug("No handlers registered for event type {EventType}; message skipped.", envelope.EventType);
            return;
        }

        Func<EventEnvelope, CancellationToken, Task>[] snapshot;
        lock (handlers)
        {
            snapshot = handlers.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try
            {
                await handler(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                _metrics.RecordFailed(envelope.EventType);
                _logger.LogError(ex, "Event handler failed for event type {EventType} (eventId={EventId}).",
                    envelope.EventType, envelope.EventId);
            }
        }
    }
}
