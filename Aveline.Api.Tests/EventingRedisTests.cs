using Aveline.Api.Infrastructure.Eventing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace Aveline.Api.Tests;

/// <summary>
/// Tests for the Redis-backed eventing components that require an
/// <see cref="IConnectionMultiplexer"/> / <see cref="ISubscriber"/>, mocked with Moq.
/// </summary>
public class RedisEventBusPublishTests
{
    private static (RedisEventBus Bus, Mock<ISubscriber> Subscriber) CreateBus()
    {
        var subscriber = new Mock<ISubscriber>();
        subscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);

        var connection = new Mock<IConnectionMultiplexer>();
        connection.Setup(c => c.GetSubscriber()).Returns(subscriber.Object);

        var bus = new RedisEventBus(
            connection.Object,
            new SystemTextJsonEventSerializer(),
            new EventBusMetrics(),
            NullLogger<RedisEventBus>.Instance);

        return (bus, subscriber);
    }

    [Fact]
    public async Task PublishAsync_PublishesSerializedEnvelopeToOrgChannel()
    {
        var (bus, subscriber) = CreateBus();
        var orgId = Guid.NewGuid();

        await bus.PublishAsync("message.received", orgId, new { text = "hi" });

        subscriber.Verify(
            s => s.PublishAsync(
                RedisChannel.Literal($"aveline:{orgId}:message.received"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishAsync_SerializesEnvelopeWithEventType()
    {
        var (bus, subscriber) = CreateBus();
        RedisValue captured = default;

        subscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) => captured = value)
            .ReturnsAsync(1L);

        await bus.PublishAsync("workflow.completed", Guid.NewGuid(), new { orderId = "ord-1" });

        var envelope = new SystemTextJsonEventSerializer().Deserialize(captured.ToString());
        Assert.NotNull(envelope);
        Assert.Equal("workflow.completed", envelope.EventType);
    }

    [Fact]
    public async Task PublishAsync_ThrowsOnBlankEventType()
    {
        var (bus, _) = CreateBus();

        await Assert.ThrowsAsync<ArgumentException>(() => bus.PublishAsync("", null, null));
    }
}

public class RedisSubscriptionServiceSubscribeTests
{
    private static (RedisSubscriptionService Service, Mock<ISubscriber> Subscriber) CreateService(string[] eventTypes)
    {
        var subscriber = new Mock<ISubscriber>();
        subscriber
            .Setup(s => s.SubscribeAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        var connection = new Mock<IConnectionMultiplexer>();
        connection.Setup(c => c.GetSubscriber()).Returns(subscriber.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Eventing:SubscribeEventTypes:0"] = eventTypes.ElementAtOrDefault(0),
                ["Eventing:SubscribeEventTypes:1"] = eventTypes.ElementAtOrDefault(1),
            })
            .Build();

        var service = new RedisSubscriptionService(
            bus: null!,
            connection: connection.Object,
            configuration: config,
            logger: NullLogger<RedisSubscriptionService>.Instance);

        return (service, subscriber);
    }

    [Fact]
    public async Task ExecuteAsync_SubscribesToConfiguredPatterns()
    {
        var (service, subscriber) = CreateService(["workflow.completed", "agent.status"]);

        await service.StartAsync(CancellationToken.None);

        // BackgroundService.StartAsync does not await ExecuteAsync, so wait until the
        // background task has established both subscriptions before verifying.
        await WaitForSubscriptionsAsync(subscriber, expectedCount: 2);

        await service.StopAsync(CancellationToken.None);

        subscriber.Verify(
            s => s.SubscribeAsync(
                RedisChannel.Pattern("aveline:*:workflow.completed"),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
        subscriber.Verify(
            s => s.SubscribeAsync(
                RedisChannel.Pattern("aveline:*:agent.status"),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    private static async Task WaitForSubscriptionsAsync(Mock<ISubscriber> subscriber, int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (subscriber.Invocations.Count(i => i.Method.Name == nameof(ISubscriber.SubscribeAsync)) >= expectedCount)
            {
                return;
            }
            await Task.Delay(10);
        }

        throw new TimeoutException($"Expected {expectedCount} SubscribeAsync invocations but saw {subscriber.Invocations.Count}.");
    }
}

public class RedisHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_RedisReachable_ReturnsHealthy()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(1));

        var connection = new Mock<IConnectionMultiplexer>();
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);

        var check = new RedisHealthCheck(connection.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_RedisUnreachable_ReturnsUnhealthy()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var connection = new Mock<IConnectionMultiplexer>();
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);

        var check = new RedisHealthCheck(connection.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
