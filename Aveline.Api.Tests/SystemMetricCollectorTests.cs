using Aveline.Api.Common.Jobs;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #228 — the system metric collector. Verifies BR-7.10 (an unknown metric is omitted,
/// never recorded as zero), the SHA-256 dimension hash contract and the bounded in-memory
/// buffer that survives a database outage.
/// </summary>
public class SystemMetricCollectorTests
{
    private static MetricSnapshot FullSnapshot() => new()
    {
        ProcessCpuSeconds = 12.5,
        WorkingSetBytes = 1024,
        GcHeapBytes = 2048,
        ThreadCount = 8,
        ThreadPoolQueueLength = 3,
        TelemetryChannelDepth = 4,
        TelemetryDropped = 1,
        EventBusPublished = 10,
        EventBusReceived = 9,
        EventBusFailed = 1,
        EventBusPublishLatencyMs = 12.5,
        ApiRequestsPerSecond = 2.5,
        ApiErrorRate = 0.02,
        AgentRunsRunning = 3,
    };

    /// <summary>A snapshot with a value for every field the collector can map.</summary>
    private static MetricSnapshot MaximalSnapshot() => FullSnapshot() with
    {
        BlossomBalance = 120.5m,
        BlossomReconciliationDrift = 0.25m,
        BlossomConsumedRate = 3.5,
        AgentSuccessRate = 0.9,
        AgentPausedCount = 2,
        AgentStepsPerRun = 7.5,
        ApiLatencyP95Ms = 420,
        DbPoolSaturation = 0.42,
    };

    [Fact]
    public void BuildSamples_EmitsExactlyTheDeclaredProducedMetricNames()
    {
        var samples = SystemMetricCollector.BuildSamples(MaximalSnapshot());

        Assert.Equal(
            SystemMetricCollector.ProducedMetricNames.OrderBy(name => name, StringComparer.Ordinal),
            samples.Select(sample => sample.MetricName).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ProducedMetricNames_CoversEverySeededAlertRule()
    {
        Assert.All(
            SystemAlertRuleSeed.Rules,
            rule => Assert.Contains(rule.MetricName, SystemMetricCollector.ProducedMetricNames));
    }

    [Fact]
    public void TelemetryDroppedRule_UsesRateNotSum()
    {
        // The dropped-sample counter is monotonic and never reset, so Sum over a window
        // latches above zero for ever after the first drop. Rate measures the per-minute
        // increase, exactly as eventbus.failed already does (§3.4).
        var rule = SystemAlertRuleSeed.Rules.Single(candidate => candidate.Name == "telemetry.dropped");

        Assert.Equal(AlertAggregation.Rate, rule.Aggregation);
    }

    [Fact]
    public void BuildSamples_OmitsMetricsThatCannotBeDetermined()
    {
        Assert.Empty(SystemMetricCollector.BuildSamples(new MetricSnapshot()));
    }

    [Fact]
    public void BuildSamples_RecordsOnlyProvidedMetrics()
    {
        var sample = Assert.Single(SystemMetricCollector.BuildSamples(new MetricSnapshot { ThreadCount = 8 }));

        Assert.Equal("aveline.process.thread_count", sample.MetricName);
        Assert.Equal(8, sample.ValueBigint);
        Assert.Null(sample.ValueDecimal);
        Assert.Equal("{}", sample.DimensionsJson);
    }

    [Fact]
    public void BuildSamples_UsesSnakeCaseAvelineNamesAndKnownUnits()
    {
        var samples = SystemMetricCollector.BuildSamples(FullSnapshot());

        Assert.All(samples, sample => Assert.StartsWith("aveline.", sample.MetricName));
        Assert.All(samples, sample => Assert.Matches("^aveline(\\.[a-z0-9_]+)+$", sample.MetricName));
        Assert.All(samples, sample => Assert.Contains(sample.Unit, new[] { "count", "ms", "bytes", "ratio", "percent" }));

        // Names the seeded rules watch must be produced by the collector.
        Assert.Contains(samples, sample => sample.MetricName == "aveline.queue.telemetry_channel");
        Assert.Contains(samples, sample => sample.MetricName == "aveline.eventbus.failed");
        Assert.Contains(samples, sample => sample.MetricName == "aveline.api.telemetry.dropped");
    }

    [Fact]
    public void EventBusPublishLatencyP95_IsOmittedBelowTheSampleFloor()
    {
        // M-8: below the floor the percentile is omitted, never computed from one observation.
        var metrics = new EventBusMetrics();
        metrics.RecordPublishLatency(50, "order.created");

        Assert.Null(metrics.LatencyP95Ms);

        for (var i = 1; i <= EventBusMetrics.MinLatencySamplesForPercentile; i++)
        {
            metrics.RecordPublishLatency(i * 10, "order.created");
        }

        Assert.NotNull(metrics.LatencyP95Ms);
        Assert.InRange(metrics.LatencyP95Ms!.Value, 10, 60);
    }

    [Fact]
    public void BuildSamples_HasExactValueColumnAndStableSixtyFourCharHash()
    {
        var first = SystemMetricCollector.BuildSamples(FullSnapshot());
        var second = SystemMetricCollector.BuildSamples(FullSnapshot());

        Assert.Equal(13, first.Count);
        Assert.All(first, sample =>
        {
            Assert.Equal(64, sample.DimensionHash.Length);
            Assert.Matches("^[0-9a-f]{64}$", sample.DimensionHash);
            Assert.True(sample.ValueDecimal is not null ^ sample.ValueBigint is not null);
        });
        Assert.Equal(
            first.Select(sample => sample.DimensionHash),
            second.Select(sample => sample.DimensionHash));
    }

    [Fact]
    public async Task RunAsync_BuffersAtMostOneHundredSamplesAndCountsTheDrops()
    {
        var repository = new StubSystemMetricRepository { Fail = true };
        await using var provider = BuildProvider(repository);

        var collector = new FixedSnapshotCollector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IDistributedJobLock>(),
            new MetricSnapshot { ThreadCount = 1 });

        for (var i = 0; i < 150; i++)
        {
            Assert.Equal(0, await collector.RunAsync(CancellationToken.None));
        }

        Assert.Equal(SystemMetricCollector.MaxBufferedSamples, collector.BufferedSampleCount);
        Assert.Equal(50, collector.DroppedSamples);
    }

    [Fact]
    public async Task RunAsync_FlushesTheBufferOnTheNextSuccessfulPass()
    {
        var repository = new StubSystemMetricRepository { Fail = true };
        await using var provider = BuildProvider(repository);

        var collector = new FixedSnapshotCollector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IDistributedJobLock>(),
            new MetricSnapshot { ThreadCount = 1 });

        Assert.Equal(0, await collector.RunAsync(CancellationToken.None));
        Assert.Equal(1, collector.BufferedSampleCount);

        repository.Fail = false;
        Assert.Equal(2, await collector.RunAsync(CancellationToken.None));
        Assert.Equal(0, collector.BufferedSampleCount);
    }

    [Fact]
    public async Task RunAsync_PublishesTheGaugesBeforeAttemptingTheDatabaseWrite()
    {
        // Plan §3.3/§9 Slice 2: a database outage must not blind the operator dashboard, so the
        // gauges are published first and the failing write must not suppress them.
        var repository = new StubSystemMetricRepository { Fail = true };
        await using var provider = BuildProvider(repository);
        using var metrics = new AvelineMetrics();

        var collector = new FixedSnapshotCollector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IDistributedJobLock>(),
            new MetricSnapshot { ThreadCount = 1, EventBusPublished = 10, EventBusReceived = 4 },
            metrics);

        Assert.Equal(0, await collector.RunAsync(CancellationToken.None));

        metrics.PublishedNames.Should().Contain("aveline.process.thread_count");
        metrics.PublishedNames.Should().Contain("aveline.eventbus.backlog");
    }

    private static ServiceProvider BuildProvider(ISystemMetricRepository repository)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"SystemMetricCollector_{Guid.NewGuid()}"));
        services.AddSingleton(repository);
        services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock());
        return services.BuildServiceProvider();
    }

    private sealed class FixedSnapshotCollector(
        IServiceScopeFactory scopeFactory,
        IDistributedJobLock jobLock,
        MetricSnapshot snapshot,
        AvelineMetrics? metrics = null)
        : SystemMetricCollector(
            scopeFactory,
            jobLock,
            NullLogger<SystemMetricCollector>.Instance,
            new ConfigurationBuilder().Build(),
            metrics)
    {
        protected override Task<MetricSnapshot> CaptureAsync(IServiceProvider services, CancellationToken cancellationToken)
            => Task.FromResult(snapshot);
    }

    private sealed class StubSystemMetricRepository : ISystemMetricRepository
    {
        public bool Fail { get; set; }

        public Task<int> UpsertAsync(IReadOnlyList<SystemMetricSample> samples, CancellationToken cancellationToken = default)
            => Fail
                ? throw new InvalidOperationException("database unavailable")
                : Task.FromResult(samples.Count);

        public Task<int> DeleteSamplesOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<SystemMetricSample>> QueryAsync(
            string metricName, DateTime from, DateTime to, string? windowSize = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SystemMetricSample>>([]);

        public Task<SystemMetricSample?> LatestAsync(string metricName, CancellationToken cancellationToken = default)
            => Task.FromResult<SystemMetricSample?>(null);

        public Task<IReadOnlyList<string>> ListMetricNamesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
