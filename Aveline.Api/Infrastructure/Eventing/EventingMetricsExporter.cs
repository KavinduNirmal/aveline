using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Periodically logs the <see cref="EventBusMetrics"/> cumulative counters as structured
/// JSON, so the metrics flow through the existing ELK / Application Insights log pipeline
/// without a separate metrics infrastructure (ADR-014). The interval is controlled by
/// <c>Eventing:MetricsLogIntervalSeconds</c>.
/// </summary>
public sealed class EventingMetricsExporter : BackgroundService
{
    private readonly EventBusMetrics _metrics;
    private readonly ILogger<EventingMetricsExporter> _logger;
    private readonly TimeSpan _interval;

    public EventingMetricsExporter(
        EventBusMetrics metrics,
        IConfiguration configuration,
        ILogger<EventingMetricsExporter> logger)
    {
        _metrics = metrics;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(configuration.GetValue("Eventing:MetricsLogIntervalSeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            LogSnapshot();
        }
    }

    private void LogSnapshot()
    {
        var snapshot = _metrics.Snapshot();
        if (snapshot.Values.All(v => v == 0))
        {
            return;
        }

        _logger.LogInformation("Event bus metrics: {Metrics}", JsonSerializer.Serialize(snapshot));
    }
}
