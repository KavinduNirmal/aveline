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

        var subscriber = _connection.GetSubscriber();
        foreach (var eventType in _eventTypes)
        {
            var pattern = EventChannel.PatternForEventType(eventType);
            await subscriber.SubscribeAsync(
                RedisChannel.Pattern(pattern),
                (channel, value) => DispatchAsync(channel, value, stoppingToken));
            _logger.LogInformation("Subscribed to Redis event pattern {Pattern}.", pattern);
        }
    }

    private async Task DispatchAsync(RedisChannel channel, RedisValue value, CancellationToken cancellationToken)
    {
        try
        {
            await _bus.DispatchAsync(channel.ToString(), value.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch Redis event on channel {Channel}.", channel);
        }
    }
}
