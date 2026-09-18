using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Daily 00:05 UTC: creates tomorrow's <c>ApiRequestLogs</c> partition and drops partitions
/// past the raw retention (default 7 days) through the M7 SQL functions
/// (domain-model.md §10.2). No-op on non-relational providers.
/// </summary>
public sealed class ApiRequestLogPartitionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<ApiRequestLogPartitionJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "api-request-log-partition";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override TimeSpan? InitialDelay =>
        AgentStatsRetentionJob.NextDailyRunAt(DateTime.UtcNow, hourUtc: 0, minuteUtc: 5);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (!db.Database.IsRelational())
        {
            return 0;
        }

        var rawDays = configuration.GetValue("Telemetry:RawLogRetentionDays", 7);
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);

        await db.Database.ExecuteSqlRawAsync(
            $"SELECT aveline_ensure_api_request_log_partition('{tomorrow:yyyy-MM-dd}'::date)",
            cancellationToken);

        var dropped = await db.Database
            .SqlQueryRaw<int>("SELECT aveline_drop_old_api_request_log_partitions({0}) AS \"Value\"", rawDays)
            .SingleAsync(cancellationToken);

        return dropped + 1;
    }
}
