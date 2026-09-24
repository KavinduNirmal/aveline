using Aveline.Api.Common.Jobs;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Metrics;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 2 acceptance (plan §8.1): after one collector pass the scrape carries every
/// <see cref="MetricsCatalog"/> business series, and the publish happens **before** the database
/// write so a database outage does not blind the operator dashboard.
/// </summary>
[Collection(MetricsExpositionCollection.Name)]
public class MetricsBridgeExpositionTests
{
    /// <summary>A snapshot with a value for every one of the twenty produced metric names.</summary>
    private static MetricSnapshot Snapshot() => new()
    {
        ProcessCpuSeconds = 12.5,
        WorkingSetBytes = 1024,
        GcHeapBytes = 2048,
        ThreadCount = 8,
        ThreadPoolQueueLength = 3,
        TelemetryChannelDepth = 4,
        TelemetryDropped = 1,
        EventBusPublished = 10,
        EventBusReceived = 4,
        EventBusFailed = 1,
        EventBusPublishLatencyMs = 12.5,
        ApiRequestsPerSecond = 2.5,
        ApiErrorRate = 0.02,
        AgentRunsRunning = 3,
        AgentRunsTotal = 42,
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
    public async Task AfterOneCollectorPassTheScrapeCarriesEveryBridgedBusinessSeries()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(AvelineMetrics.MeterName)
            .AddPrometheusExporter());

        var app = builder.Build();
        app.MapPrometheusScrapingEndpoint("/metrics");

        await app.StartAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"MetricsBridge_{Guid.NewGuid()}"));
            services.AddSingleton<ISystemMetricRepository>(new FailingRepository());
            services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock());
            await using var provider = services.BuildServiceProvider();

            using var metricsInstance = new AvelineMetrics();
            var collector = new StubSnapshotCollector(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IDistributedJobLock>(),
                metricsInstance,
                Snapshot());

            collector.PublishToMetrics(Snapshot());

            var client = app.GetTestClient();
            var body = await client.GetStringAsync("/metrics");
            var names = MetricsNamingTests.SampleNames(body);

            foreach (var metric in MetricsCatalog.BusinessMetrics)
            {
                names.Should().Contain(metric.PrometheusName);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task AGaugeIsAbsentRatherThanZeroWhenTheSnapshotOmitsIt()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(AvelineMetrics.MeterName)
            .AddPrometheusExporter());

        var app = builder.Build();
        app.MapPrometheusScrapingEndpoint("/metrics");

        await app.StartAsync();
        try
        {
            using var metricsInstance = new AvelineMetrics();
            metricsInstance.Publish(MetricSnapshotReader.Flatten(new MetricSnapshot { ThreadCount = 3 }));

            var client = app.GetTestClient();
            var names = MetricsNamingTests.SampleNames(await client.GetStringAsync("/metrics"));

            names.Should().Contain("aveline_process_thread_count");
            names.Should().NotContain(
                "aveline_blossom_balance_count",
                "an unknown metric is omitted, never zero (BR-7.10)");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class StubSnapshotCollector(
        IServiceScopeFactory scopeFactory,
        IDistributedJobLock jobLock,
        AvelineMetrics metrics,
        MetricSnapshot snapshot)
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

    private sealed class FailingRepository : ISystemMetricRepository
    {
        public Task<int> UpsertAsync(IReadOnlyList<SystemMetricSample> samples, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("database unavailable");

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
