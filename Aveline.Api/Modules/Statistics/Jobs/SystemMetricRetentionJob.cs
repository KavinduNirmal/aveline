using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Daily 03:30 UTC retention (BR-7.9, S-35): deletes <c>SystemMetricSamples</c> older than
/// <c>Observability:SystemMetricRetentionDays</c> (default 30). Idempotent and re-runnable.
/// </summary>
public sealed class SystemMetricRetentionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<SystemMetricRetentionJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "system-metric-retention";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override TimeSpan? InitialDelay =>
        AgentStatsRetentionJob.NextDailyRunAt(DateTime.UtcNow, hourUtc: 3, minuteUtc: 30);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISystemMetricRepository>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var retentionDays = configuration.GetValue("Observability:SystemMetricRetentionDays", 30);
        return await repository.DeleteSamplesOlderThanAsync(
            DateTime.UtcNow.AddDays(-retentionDays), cancellationToken);
    }
}
