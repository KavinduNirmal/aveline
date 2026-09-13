using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #224 — amortised API-key usage: LastUsedAt, RequestCount and LastUsedIpHash are
/// flushed to ApiKeys and <c>apikey.lastused</c> is published (FR-3.18).
/// </summary>
public class ApiKeyUsageAggregatorTests
{
    private sealed class RecordingEventBus : IEventBus
    {
        public List<(string Type, Guid? OrganizationId, object? Payload)> Published { get; } = [];

        public Task PublishAsync(
            string eventType, Guid? organizationId, object? payload, Guid? traceId = null,
            CancellationToken cancellationToken = default)
        {
            Published.Add((eventType, organizationId, payload));
            return Task.CompletedTask;
        }

        public Task SubscribeAsync(
            string eventType, Func<EventEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UnsubscribeAsync(
            string eventType, Func<EventEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record Harness(
        ApiKeyUsageAggregator Aggregator,
        RecordingEventBus Bus,
        string DatabaseName,
        ServiceProvider Provider)
    {
        public AppDbContext Context() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DatabaseName).Options);
    }

    private static Harness Build()
    {
        var databaseName = $"KeyUsage_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();

        var bus = new RecordingEventBus();
        var aggregator = new ApiKeyUsageAggregator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            bus,
            NullLogger<ApiKeyUsageAggregator>.Instance);

        return new Harness(aggregator, bus, databaseName, provider);
    }

    private static ApiKey SeedKey(Harness harness)
    {
        var organizationId = Guid.CreateVersion7();
        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Name = "Primary",
            Prefix = $"avl_live_{Guid.NewGuid():N}"[..24],
            KeyHash = new string('a', 64),
        };

        using var context = harness.Context();
        context.ApiKeys.Add(key);
        context.SaveChanges();
        return key;
    }

    [Fact]
    public async Task FlushAggregatesCountAndLastUsedAndPublishesOnce()
    {
        var harness = Build();
        var key = SeedKey(harness);
        var first = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var second = first.AddSeconds(30);

        harness.Aggregator.Record(key.Id, first, "hash-one");
        harness.Aggregator.Record(key.Id, second, "hash-two");

        Assert.Equal(1, await harness.Aggregator.FlushAsync());

        using var context = harness.Context();
        var persisted = context.ApiKeys.Single();
        Assert.Equal(2, persisted.RequestCount);
        Assert.Equal(second, persisted.LastUsedAt);
        Assert.Equal("hash-one", persisted.LastUsedIpHash);

        Assert.Single(harness.Bus.Published, e => e.Type == "apikey.lastused");
        harness.Provider.Dispose();
    }

    [Fact]
    public async Task FlushWithNothingPendingIsANoOp()
    {
        var harness = Build();
        SeedKey(harness);

        Assert.Equal(0, await harness.Aggregator.FlushAsync());
        Assert.Empty(harness.Bus.Published);
        harness.Provider.Dispose();
    }

    [Fact]
    public async Task FlushOfAnUnknownKeyDoesNotThrowOrPublish()
    {
        var harness = Build();
        harness.Aggregator.Record(Guid.CreateVersion7(), DateTime.UtcNow, null);

        Assert.Equal(0, await harness.Aggregator.FlushAsync());
        Assert.Empty(harness.Bus.Published);
        harness.Provider.Dispose();
    }

    [Fact]
    public async Task ASecondFlushDoesNotDoubleCount()
    {
        var harness = Build();
        var key = SeedKey(harness);

        harness.Aggregator.Record(key.Id, DateTime.UtcNow, null);
        Assert.Equal(1, await harness.Aggregator.FlushAsync());
        Assert.Equal(0, await harness.Aggregator.FlushAsync());

        using var context = harness.Context();
        Assert.Equal(1, context.ApiKeys.Single().RequestCount);
        harness.Provider.Dispose();
    }
}
