using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Long-running host that owns the Redis <c>PSUBSCRIBE</c> connections for the event bus
/// (ADR-014). On startup it subscribes to the event types listed in
/// <c>Eventing:SubscribeEventTypes</c> (each mapped to the <c>aveline:*:&lt;event_type&gt;</c>
/// pattern) and dispatches every received message into <see cref="RedisEventBus.DispatchAsync"/>,
/// which fans it out to the handlers registered for that event type.
///
/// <para>
/// Messages are handed to a single consumer that awaits each handler before taking the next
/// message, so events reach handlers in the order Redis delivered them. Redis delivers pub/sub
/// messages in publish order, but StackExchange.Redis invokes the subscriber callback as a
/// synchronous <see cref="Action{T1,T2}"/> and never awaits it, so doing the async dispatch
/// inside that callback makes every event race: the workflow's <c>agent.status</c> events and
/// the <c>message.created</c> events they describe then arrive at clients interleaved or
/// reversed (observed as a "thinking" state landing after the reply it belonged to, leaving a
/// progress bubble hanging that nothing could close).
/// </para>
///
/// <para>
/// StackExchange.Redis automatically re-establishes subscriptions when the connection is
/// restored, so no manual reconnect loop is required. Malformed messages are handled (logged
/// and skipped) inside <see cref="RedisEventBus.DispatchAsync"/>.
/// </para>
/// </summary>
public sealed class RedisSubscriptionService : BackgroundService
{
    private readonly RedisEventBus _bus;
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisSubscriptionService> _logger;
    private readonly string[] _eventTypes;

    public RedisSubscriptionService(
        RedisEventBus bus,
        IConnectionMultiplexer connection,
        IConfiguration configuration,
        ILogger<RedisSubscriptionService> logger)
    {
        _bus = bus;
        _connection = connection;
        _logger = logger;
        _eventTypes = configuration.GetSection("Eventing:SubscribeEventTypes").Get<string[]>() ?? [];
    }

    /// <summary>The event types this host is configured to subscribe to.</summary>
    public IReadOnlyCollection<string> EventTypes => _eventTypes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_eventTypes.Length == 0)
        {
            _logger.LogInformation("No event types configured for subscription; RedisSubscriptionService is idle.");
            return;
        }

        // Unbounded on purpose: the Redis callback cannot block or signal back-pressure, and
        // dropping events to stay bounded would silently lose messages.
        var queue = Channel.CreateUnbounded<RedisMessage>(
            new UnboundedChannelOptions { SingleReader = true });

        var subscriber = _connection.GetSubscriber();
        foreach (var eventType in _eventTypes)
        {
            var pattern = EventChannel.PatternForEventType(eventType);
            await subscriber.SubscribeAsync(
                RedisChannel.Pattern(pattern),
                // Enqueue only; the consumer below awaits handlers in arrival order.
                (channel, value) => queue.Writer.TryWrite(new RedisMessage(channel.ToString(), value.ToString())));
            _logger.LogInformation("Subscribed to Redis event pattern {Pattern}.", pattern);
        }

        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(message.Channel, message.Value, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown: stop draining. Undispatched messages are dropped with the process.
        }
    }

    private async Task DispatchAsync(string channel, string value, CancellationToken cancellationToken)
    {
        try
        {
            await _bus.DispatchAsync(channel, value, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch Redis event on channel {Channel}.", channel);
        }
    }

    /// <summary>A raw pub/sub message as received, queued for in-order dispatch.</summary>
    private readonly record struct RedisMessage(string Channel, string Value);
}
