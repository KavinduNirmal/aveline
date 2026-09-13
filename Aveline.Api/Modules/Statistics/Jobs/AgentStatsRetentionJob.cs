using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Daily 03:00 UTC retention (BR-5.9): deletes steps older than
/// <c>AgentStats:StepRetentionDays</c> (default 90) and runs older than
/// <c>AgentStats:RunRetentionDays</c> (default 400). Idempotent and re-runnable.
/// </summary>
public sealed class AgentStatsRetentionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<AgentStatsRetentionJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "agent-stats-retention";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override TimeSpan? InitialDelay =>
        NextDailyRunAt(DateTime.UtcNow, hourUtc: 3, minuteUtc: 0);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var stepRetentionDays = configuration.GetValue("AgentStats:StepRetentionDays", 90);
        var runRetentionDays = configuration.GetValue("AgentStats:RunRetentionDays", 400);
        var now = DateTime.UtcNow;

        var stepsDeleted = await repository.DeleteStepsOlderThanAsync(
            now.AddDays(-stepRetentionDays), cancellationToken);
        var runsDeleted = await repository.DeleteRunsOlderThanAsync(
            now.AddDays(-runRetentionDays), cancellationToken);

        return stepsDeleted + runsDeleted;
    }

    /// <summary>Exposed for tests: the delay from <paramref name="now"/> to the next daily occurrence.</summary>
    internal static TimeSpan NextDailyRunAt(DateTime now, int hourUtc, int minuteUtc)
    {
        var next = new DateTime(now.Year, now.Month, now.Day, hourUtc, minuteUtc, 0, DateTimeKind.Utc);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
