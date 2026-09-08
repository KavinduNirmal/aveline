using System.Text.Json;
using Aveline.Api.Infrastructure.Eventing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

public class EventChannelTests
{
    [Fact]
    public void ForOrganization_BuildsOrgScopedChannel()
    {
        var orgId = Guid.NewGuid();

        var channel = EventChannel.ForOrganization(orgId, "message.received");

        Assert.Equal($"aveline:{orgId}:message.received", channel);
    }

    [Fact]
    public void PatternForEventType_MatchesAnyOrganization()
    {
        var pattern = EventChannel.PatternForEventType("workflow.completed");

        Assert.Equal("aveline:*:workflow.completed", pattern);
    }
}

public class EventEnvelopeTests
{
    [Fact]
    public void Create_GeneratesIdAndTimestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var envelope = EventEnvelope.Create("message.received", Guid.NewGuid(), new { text = "hi" });

        Assert.NotEqual(Guid.Empty, envelope.EventId);
        Assert.True(envelope.Timestamp >= before);
        Assert.Equal("message.received", envelope.EventType);
    }

    [Fact]
    public void Create_ThrowsOnBlankEventType()
    {
        Assert.Throws<ArgumentException>(() => EventEnvelope.Create("", null, null));
    }
}

public class SystemTextJsonEventSerializerTests
{
    private readonly SystemTextJsonEventSerializer _serializer = new();

    [Fact]
    public void Serialize_UsesSnakeCaseKeys()
    {
        var envelope = EventEnvelope.Create("message.received", Guid.NewGuid(), new { customerId = "c-1" });

        var json = _serializer.Serialize(envelope);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("event_id", out _));
        Assert.True(doc.RootElement.TryGetProperty("event_type", out _));
        Assert.True(doc.RootElement.TryGetProperty("timestamp", out _));
        Assert.True(doc.RootElement.TryGetProperty("org_id", out _));
        Assert.True(doc.RootElement.TryGetProperty("trace_id", out _));
        Assert.True(doc.RootElement.TryGetProperty("payload", out _));
    }

    [Fact]
    public void RoundTrip_PreservesEnvelopeFields()
    {
        var orgId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        var envelope = EventEnvelope.Create("workflow.completed", orgId, new { orderId = "ord-1" }, traceId);

        var json = _serializer.Serialize(envelope);
        var deserialized = _serializer.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(envelope.EventId, deserialized.EventId);
        Assert.Equal(envelope.EventType, deserialized.EventType);
        Assert.Equal(orgId, deserialized.OrganizationId);
        Assert.Equal(traceId, deserialized.TraceId);
        Assert.Equal("ord-1", ((JsonElement)deserialized.Payload!).GetProperty("order_id").GetString());
    }

    [Fact]
    public void Deserialize_MalformedJson_ReturnsNull()
    {
        Assert.Null(_serializer.Deserialize("not json"));
    }

    [Fact]
    public void Deserialize_MissingEventType_ReturnsNull()
    {
        Assert.Null(_serializer.Deserialize("{\"event_id\":\"00000000-0000-0000-0000-000000000000\"}"));
    }
}

public class RedisEventBusTests
{
    // DispatchAsync / SubscribeAsync / UnsubscribeAsync never touch the Redis connection,
    // so a null connection is safe for these unit tests.
    private static RedisEventBus CreateBus()
        => new(
            connection: null!,
            serializer: new SystemTextJsonEventSerializer(),
            metrics: new EventBusMetrics(),
            logger: NullLogger<RedisEventBus>.Instance);

    private static string Serialize(EventEnvelope envelope)
        => new SystemTextJsonEventSerializer().Serialize(envelope);

    [Fact]
    public async Task SubscribeAndDispatch_InvokesHandlerForMatchingEventType()
    {
        var bus = CreateBus();
        var received = new List<EventEnvelope>();
        await bus.SubscribeAsync("workflow.completed", (e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        var envelope = EventEnvelope.Create("workflow.completed", Guid.NewGuid(), new { orderId = "ord-1" });
        await bus.DispatchAsync("aveline:org:workflow.completed", Serialize(envelope));

        Assert.Single(received);
        Assert.Equal(envelope.EventId, received[0].EventId);
    }

    [Fact]
    public async Task Dispatch_MalformedMessage_IsSkippedWithoutInvokingHandlers()
    {
        var bus = CreateBus();
        var invoked = false;
        await bus.SubscribeAsync("workflow.completed", (_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await bus.DispatchAsync("aveline:org:workflow.completed", "not json");

        Assert.False(invoked);
    }

    [Fact]
    public async Task Dispatch_NoHandlerForEventType_CompletesWithoutError()
    {
        var bus = CreateBus();
        var envelope = EventEnvelope.Create("unknown.event", Guid.NewGuid(), null);

        await bus.DispatchAsync("aveline:org:unknown.event", Serialize(envelope));
    }

    [Fact]
    public async Task Dispatch_HandlerException_DoesNotAbortOtherHandlers()
    {
        var bus = CreateBus();
        var secondInvoked = false;
        await bus.SubscribeAsync("workflow.completed", (_, _) => throw new InvalidOperationException("boom"));
        await bus.SubscribeAsync("workflow.completed", (_, _) =>
        {
            secondInvoked = true;
            return Task.CompletedTask;
        });

        var envelope = EventEnvelope.Create("workflow.completed", Guid.NewGuid(), null);
        await bus.DispatchAsync("aveline:org:workflow.completed", Serialize(envelope));

        Assert.True(secondInvoked);
    }

    [Fact]
    public async Task Unsubscribe_RemovesHandler()
    {
        var bus = CreateBus();
        var invoked = 0;
        Func<EventEnvelope, CancellationToken, Task> handler = (_, _) =>
        {
            invoked++;
            return Task.CompletedTask;
        };
        await bus.SubscribeAsync("workflow.completed", handler);
        await bus.UnsubscribeAsync("workflow.completed", handler);

        var envelope = EventEnvelope.Create("workflow.completed", Guid.NewGuid(), null);
        await bus.DispatchAsync("aveline:org:workflow.completed", Serialize(envelope));

        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task SubscribedEventTypes_ReflectsRegisteredHandlers()
    {
        var bus = CreateBus();
        await bus.SubscribeAsync("workflow.completed", (_, _) => Task.CompletedTask);

        Assert.Contains("workflow.completed", bus.SubscribedEventTypes);
    }
}

public class InMemoryEventBusTests
{
    [Fact]
    public async Task Publish_DeliversToRegisteredHandler()
    {
        var bus = new InMemoryEventBus();
        var received = new List<EventEnvelope>();
        await bus.SubscribeAsync("message.received", (e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        await bus.PublishAsync("message.received", Guid.NewGuid(), new { text = "hi" });

        Assert.Single(received);
        Assert.Equal("message.received", received[0].EventType);
    }

    [Fact]
    public async Task Publish_NoHandler_CompletesWithoutError()
    {
        var bus = new InMemoryEventBus();

        await bus.PublishAsync("message.received", Guid.NewGuid(), new { text = "hi" });
    }
}

public class RedisSubscriptionServiceTests
{
    private static RedisSubscriptionService CreateService(string[] eventTypes)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Eventing:SubscribeEventTypes:0"] = eventTypes.ElementAtOrDefault(0),
                ["Eventing:SubscribeEventTypes:1"] = eventTypes.ElementAtOrDefault(1),
            })
            .Build();

        return new RedisSubscriptionService(
            bus: null!,
            connection: null!,
            configuration: config,
            logger: NullLogger<RedisSubscriptionService>.Instance);
    }

    [Fact]
    public void EventTypes_ReflectsConfiguredEventTypes()
    {
        var service = CreateService(["workflow.completed", "agent.status"]);

        Assert.Equal(2, service.EventTypes.Count);
        Assert.Contains("workflow.completed", service.EventTypes);
        Assert.Contains("agent.status", service.EventTypes);
    }

    [Fact]
    public async Task ExecuteAsync_NoConfiguredEventTypes_CompletesWithoutError()
    {
        var service = CreateService([]);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }
}

public class EventingMetricsExporterTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task ExecuteAsync_LogsMetricsSnapshotWhenCountersAreRecorded()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Eventing:MetricsLogIntervalSeconds"] = "1",
            })
            .Build();
        var logger = new CapturingLogger<EventingMetricsExporter>();
        var metrics = new EventBusMetrics();
        var exporter = new EventingMetricsExporter(metrics, config, logger);

        await exporter.StartAsync(CancellationToken.None);
        metrics.RecordPublished("message.received");
        metrics.RecordReceived("message.received");

        // Wait for the periodic timer (1s interval) to flush a snapshot.
        await Task.Delay(TimeSpan.FromMilliseconds(1600));

        await exporter.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Messages, m => m.Contains("Event bus metrics", StringComparison.Ordinal));
    }
}
