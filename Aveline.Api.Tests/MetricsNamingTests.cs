using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Notifications.Metrics;
using Aveline.Api.Modules.Notifications.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 1's naming contract (plan §1 F-3, §6.1, §9 Slice 1). Booting the pinned exporter and
/// asserting every <see cref="ExportedMetric.PrometheusName"/> turns the plan's hand-applied
/// suffix table into an executed fact. The hand-applied table was wrong in three of nineteen
/// business rows (cpu_seconds, error_rate, success_rate); this test is what caught it, and it is
/// what will catch an exporter upgrade that changes the suffix rule before a dashboard empties.
/// </summary>
[Collection(MetricsExpositionCollection.Name)]
public class MetricsNamingTests
{
    [Fact]
    public async Task EveryExportedMetricIsScrapedUnderItsPrometheusName()
    {
        var names = TypeNames(await ScrapeAsync());

        foreach (var metric in MetricsCatalog.All)
        {
            names.Should().Contain(
                metric.PrometheusName,
                $"the exporter translates '{metric.DottedName}' (unit '{metric.Unit}') to "
                + $"'{metric.PrometheusName}', not to the dotted instrument name");
        }
    }

    [Fact]
    public async Task HistogramExportsBucketSumAndCountSeries()
    {
        var body = await ScrapeAsync();

        foreach (var metric in MetricsCatalog.All.Where(m => m.Kind == ExportedMetricKind.Histogram))
        {
            // A histogram publishes one TYPE line for the base name and three sample families.
            body.Should().Contain($"{metric.PrometheusName}_bucket");
            body.Should().Contain($"{metric.PrometheusName}_sum");
            body.Should().Contain($"{metric.PrometheusName}_count");
        }
    }

    [Fact]
    public void EveryBusinessMetricNameIsInTheExportedCatalog()
    {
        // The alert-rule guard reads the collector's ProducedMetricNames (plan N-2), so the
        // catalog and the collector must agree on the dotted keys.
        MetricsCatalog.BusinessMetrics
            .Select(metric => metric.DottedName)
            .Should()
            .BeEquivalentTo(SystemMetricCollectorProducedNames);
    }

    [Fact]
    public void PrometheusNamesAreUniqueAndPrometheusSafe()
    {
        MetricsCatalog.All.Select(metric => metric.PrometheusName).Should().OnlyHaveUniqueItems();
        MetricsCatalog.All.Select(metric => metric.DottedName).Should().OnlyHaveUniqueItems();

        foreach (var metric in MetricsCatalog.All)
        {
            metric.PrometheusName.Should().MatchRegex("^[a-zA-Z_:][a-zA-Z0-9_:]*$");
        }
    }

    /// <summary>
    /// The dotted names the collector can persist; duplicated here rather than referenced so the
    /// assertion fails if either list moves without the other.
    /// </summary>
    private static readonly string[] SystemMetricCollectorProducedNames =
    [
        "aveline.process.cpu_seconds",
        "aveline.process.working_set_bytes",
        "aveline.process.gc_heap_bytes",
        "aveline.process.thread_count",
        "aveline.process.threadpool_queue_length",
        "aveline.queue.telemetry_channel",
        "aveline.api.telemetry.dropped",
        "aveline.eventbus.failed",
        "aveline.eventbus.backlog",
        "aveline.eventbus.publish_latency_ms",
        "aveline.api.requests_per_second",
        "aveline.api.error_rate",
        "aveline.api.latency_p95",
        "aveline.agent.runs_running",
        "aveline.agent.success_rate",
        "aveline.agent.paused_count",
        "aveline.agent.steps_per_run",
        "aveline.agent.runs_total",
        "aveline.blossom.balance",
        "aveline.blossom.reconciliation.drift",
        "aveline.blossom.consumed_rate",
        "aveline.db.pool.saturation",
    ];

    private static async Task<string> ScrapeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(AvelineMetrics.MeterName)
            .AddMeter(EventBusMetrics.MeterName)
            .AddPrometheusExporter());

        var app = builder.Build();
        app.MapPrometheusScrapingEndpoint("/metrics");

        await app.StartAsync();
        try
        {
            // The instruments live on a real meter; the data is irrelevant, only the names are.
            using var metricsInstance = new AvelineMetrics();
            metricsInstance.Publish(
                MetricsCatalog.BusinessMetrics
                    .ToDictionary(metric => metric.DottedName, _ => (double?)1));

            var eventBus = new EventBusMetrics();
            eventBus.RecordPublished("order.created");
            eventBus.RecordReceived("order.created");
            eventBus.RecordFailed("order.created");
            eventBus.RecordPublishLatency(4.2, "order.created");

            // Slice 7's family: every notification series must be scraped under its catalog name.
            using var notificationMetrics = new NotificationMetrics(fcmCredentialConfigured: true);
            notificationMetrics.Publish(new NotificationMetricSnapshot(
                FcmCredentialConfigured: true,
                DeliverySuccessRateByChannel: new Dictionary<string, double> { ["Realtime"] = 1 },
                InboxReadRateByType: new Dictionary<string, double> { ["NewMessage"] = 1 },
                DismissRateByType: new Dictionary<string, double> { ["NewMessage"] = 0 },
                TimeToReadP50Minutes: 4.2,
                FailureReasonsByChannelAndClass: new Dictionary<string, long> { ["Push|timeout"] = 1 },
                VolumeByType: new Dictionary<string, long> { ["NewMessage"] = 3 },
                InboxBacklog: 2,
                PushDispatchFailuresByPlatform: new Dictionary<string, long> { ["Android"] = 1 }));
            notificationMetrics.RecordDelivery(NotificationChannel.Push, DeliveryStatus.Delivered);

            var client = app.GetTestClient();
            return await client.GetStringAsync("/metrics");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    internal static IReadOnlyList<string> TypeNames(string exposition)
        => exposition
            .Split('\n')
            .Where(line => line.StartsWith("# TYPE ", StringComparison.Ordinal))
            .Select(line => line.Split(' ')[2])
            .ToArray();

    /// <summary>
    /// The series that actually carry a sample. A registered instrument still emits a <c># TYPE</c>
    /// line with no observations, so only sample lines prove a value exists (BR-7.10).
    /// </summary>
    internal static IReadOnlyList<string> SampleNames(string exposition)
        => exposition
            .Split('\n')
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line[..line.IndexOfAny([' ', '{'])])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
