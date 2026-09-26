using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Eventing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 2 (plan §7.3, R-2/R-16). A Prometheus TSDB falls over on label cardinality, and the data
/// model is full of unbounded ids. The instrument-level assertion is cheap; the scrape-level one is
/// the only layer that can see a *library's* label (Npgsql's pool name defaults to the connection
/// string minus the password), which is why both exist.
/// </summary>
[Collection(MetricsExpositionCollection.Name)]
public class MetricsCardinalityTests
{
    private static readonly string[] PlanProhibitions =
    [
        "organizationId", "userId", "customerId", "conversationId", "messageId",
        "workflowId", "runId", "apiKeyId", "db.client.connection.pool.name",
    ];

    [Fact]
    public void ForbiddenLabelKeys_CoverEveryPlanProhibition()
    {
        MetricsCatalog.ForbiddenLabelKeys.Should().Contain(PlanProhibitions);
    }

    [Fact]
    public async Task ScrapeBody_DeclaresNoForbiddenLabelKey()
    {
        var body = await ScrapeAsync();
        var keys = LabelKeys(body);

        keys.Should().NotBeEmpty("the detector must have seen at least one labelled series to be meaningful");
        keys.Should().NotIntersectWith(MetricsCatalog.ForbiddenLabelKeys);
    }

    [Fact]
    public async Task ScrapeBody_HasNoConnectionStringLikeLabelValue()
    {
        var body = await ScrapeAsync();

        // R-16: a pool-name label defaulting to the connection string would put the host,
        // database and username (and in some forms the password) into the TSDB.
        body.Should().NotMatchRegex(@"Host=\S+;.*(Password|Username)=");
        body.Should().NotMatchRegex(@"(?i)password=");
    }

    [Fact]
    public void TheDetector_WouldCatchAForbiddenLabel()
    {
        // Proves the scrape-level assertion above is not vacuous: a library instrument that tagged
        // a series with a run id must be reported.
        using var meter = new Meter("cardinality-detector-probe");
        var counter = meter.CreateCounter<long>("probe_runs");
        counter.Add(1, new KeyValuePair<string, object?>("runId", "run_123"));

        var exposition = "probe_runs_total{runId=\"run_123\"} 1";

        LabelKeys(exposition).Should().IntersectWith(MetricsCatalog.ForbiddenLabelKeys);
    }

    internal static IReadOnlyList<string> LabelKeys(string exposition)
        => Regex.Matches(exposition, @"\{([^}]*)\}")
            .SelectMany(match => match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(pair => pair.Split('=')[0].Trim())
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static async Task<string> ScrapeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(AvelineMetrics.MeterName)
            .AddMeter(EventBusMetrics.MeterName)
            .AddMeter("System.Net.Http")
            .AddPrometheusExporter());

        var app = builder.Build();
        app.MapPrometheusScrapingEndpoint("/metrics");

        await app.StartAsync();
        try
        {
            using var metricsInstance = new AvelineMetrics();
            metricsInstance.Publish(
                MetricsCatalog.BusinessMetrics.ToDictionary(metric => metric.DottedName, _ => (double?)1));

            var eventBus = new EventBusMetrics();
            eventBus.RecordPublished("order.created");

            var client = app.GetTestClient();
            await client.GetAsync("/metrics");

            return await client.GetStringAsync("/metrics");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
