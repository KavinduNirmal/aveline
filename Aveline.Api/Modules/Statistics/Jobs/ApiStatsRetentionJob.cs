using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Daily retention (FR-6.4): raw request logs 7 days, hourly rollups 90 days and daily
/// rollups 400 days. Partitioned raw logs are dropped by partition when PostgreSQL is in
/// use, otherwise by row. Idempotent and re-runnable.
/// </summary>
public sealed class ApiStatsRetentionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<ApiStatsRetentionJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "api-stats-retention";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override TimeSpan? InitialDelay =>
        AgentStatsRetentionJob.NextDailyRunAt(DateTime.UtcNow, hourUtc: 3, minuteUtc: 30);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var rawDays = configuration.GetValue("Telemetry:RawLogRetentionDays", 7);
        var hourlyDays = configuration.GetValue("Telemetry:HourlyRollupRetentionDays", 90);
        var dailyDays = configuration.GetValue("Telemetry:DailyRollupRetentionDays", 400);
        var now = DateTime.UtcNow;

        var deleted = 0;

        if (db.Database.IsRelational())
        {
            // Partition drop is the efficient raw-log retention path.
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT aveline_drop_old_api_request_log_partitions({rawDays})", cancellationToken);

            deleted += await db.ApiRequestMetrics
                .Where(row => row.WindowSize == "hour" && row.WindowStart < now.AddDays(-hourlyDays))
                .ExecuteDeleteAsync(cancellationToken);

            deleted += await db.ApiRequestMetrics
                .Where(row => row.WindowSize == "day" && row.WindowStart < now.AddDays(-dailyDays))
                .ExecuteDeleteAsync(cancellationToken);

            return deleted;
        }

        var rawCutoff = now.AddDays(-rawDays);
        var oldLogs = await db.ApiRequestLogs
            .Where(log => log.OccurredAt < rawCutoff)
            .ToListAsync(cancellationToken);
        if (oldLogs.Count > 0)
        {
            db.ApiRequestLogs.RemoveRange(oldLogs);
            deleted += oldLogs.Count;
        }

        var oldHourly = await db.ApiRequestMetrics
            .Where(row => row.WindowSize == "hour" && row.WindowStart < now.AddDays(-hourlyDays))
            .ToListAsync(cancellationToken);
        if (oldHourly.Count > 0)
        {
            db.ApiRequestMetrics.RemoveRange(oldHourly);
            deleted += oldHourly.Count;
        }

        var oldDaily = await db.ApiRequestMetrics
            .Where(row => row.WindowSize == "day" && row.WindowStart < now.AddDays(-dailyDays))
            .ToListAsync(cancellationToken);
        if (oldDaily.Count > 0)
        {
            db.ApiRequestMetrics.RemoveRange(oldDaily);
            deleted += oldDaily.Count;
        }

        await db.SaveChangesAsync(cancellationToken);
        return deleted;
    }
}
