using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Drains <see cref="TelemetryChannel"/> in batches of <c>Telemetry:WriterBatchSize</c>
/// (500), upserts the hourly rollup and writes the sampled raw log (FR-6.4, BR-6.3/6.4).
/// Rollups are never sampled.
/// </summary>
public sealed class ApiTelemetryWriter(
    TelemetryChannel channel,
    IServiceScopeFactory scopeFactory,
    IOptions<TelemetryOptions> options,
    ILogger<ApiTelemetryWriter> logger) : BackgroundService
{
    private readonly TelemetryOptions _options = options.Value;

    /// <summary>Drains one batch. Public so tests can drive it deterministically.</summary>
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Max(1, _options.WriterBatchSize);
        var batch = new List<ApiRequestSample>(batchSize);
        while (batch.Count < batchSize && channel.TryRead(out var sample))
        {
            batch.Add(sample);
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        var (metrics, logs) = ApiRequestAggregator.Aggregate(batch);

        using var scope = scopeFactory.CreateScope();
        var metricRepository = scope.ServiceProvider.GetRequiredService<IApiMetricRepository>();
        var logRepository = scope.ServiceProvider.GetRequiredService<IApiRequestLogRepository>();

        await metricRepository.UpsertAsync(metrics, cancellationToken);
        await logRepository.InsertAsync(logs, cancellationToken);

        return batch.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var flushInterval = TimeSpan.FromSeconds(Math.Max(1, _options.WriterFlushSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DrainOnceAsync(stoppingToken) == 0)
                {
                    // Idle: wait for a sample but never longer than the flush interval.
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    idle.CancelAfter(flushInterval);
                    try
                    {
                        await channel.WaitToReadAsync(idle.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Either stopping or the flush interval elapsed; loop again.
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telemetry writer batch failed.");
                try
                {
                    await Task.Delay(flushInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
