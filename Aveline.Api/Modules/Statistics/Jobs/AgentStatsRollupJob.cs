using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Daily 01:00 UTC job that aggregates previous day's AgentWorkflowRuns into DailyAgentMetrics.
/// Idempotent (delete-and-replace per day).
/// </summary>
public sealed class AgentStatsRollupJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<AgentStatsRollupJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "agent-stats-rollup";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override TimeSpan? InitialDelay => NextDailyRunAt(DateTime.UtcNow, hourUtc: 1, minuteUtc: 0);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        return await RecomputeDayAsync(db, yesterday, cancellationToken);
    }

    public static async Task<int> RecomputeDayAsync(
        AppDbContext db, DateTime day, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var runs = await db.AgentWorkflowRuns
            .Where(r => r.StartedAt >= start && r.StartedAt < end)
            .ToListAsync(cancellationToken);

        if (runs.Count == 0)
        {
            return 0;
        }

        var existing = await db.DailyAgentMetrics
            .Where(m => m.Day == start)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            db.DailyAgentMetrics.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var recomputed = runs
            .GroupBy(r => new { r.OrganizationId })
            .Select(group =>
            {
                var list = group.ToList();
                var durations = list.Where(r => r.DurationMs.HasValue).Select(r => (double)r.DurationMs!.Value).ToArray();

                return new DailyAgentMetric
                {
                    Id = Guid.CreateVersion7(),
                    OrganizationId = group.Key.OrganizationId,
                    AgentKey = "orchestrator",
                    Day = start,
                    RunCount = list.Count,
                    SucceededCount = list.Count(r => r.Status == AgentRunStatus.Succeeded),
                    FailedCount = list.Count(r => r.Status == AgentRunStatus.Failed),
                    PausedCount = list.Count(r => r.Status == AgentRunStatus.PausedForApproval),
                    TimedOutCount = list.Count(r => r.Status == AgentRunStatus.TimedOut),
                    CancelledCount = list.Count(r => r.Status == AgentRunStatus.Cancelled),
                    TotalDurationMs = list.Sum(r => (long)(r.DurationMs ?? 0)),
                    MaxDurationMs = list.Count == 0 ? 0 : list.Max(r => r.DurationMs ?? 0),
                    AvgDurationMs = durations.Length > 0 ? durations.Average() : null,
                    P50DurationMs = durations.Length > 0 ? PercentileCalculator.Percentile(durations, 0.50) : null,
                    P95DurationMs = durations.Length > 0 ? PercentileCalculator.Percentile(durations, 0.95) : null,
                    P99DurationMs = durations.Length > 0 ? PercentileCalculator.Percentile(durations, 0.99) : null,
                    StepCount = list.Sum(r => r.StepCount),
                    ToolCallCount = list.Sum(r => r.ToolCallCount),
                    RetryCount = list.Sum(r => r.RetryCount),
                    InputTokens = list.Sum(r => (long)r.InputTokens),
                    OutputTokens = list.Sum(r => (long)r.OutputTokens),
                    CachedTokens = list.Sum(r => (long)r.CachedTokens),
                    ActualCostUsd = list.Sum(r => r.ActualCostUsd),
                    BlossomUnits = list.Sum(r => r.BlossomUnits),
                    CreatedAt = DateTime.UtcNow,
                };
            })
            .ToList();

        db.DailyAgentMetrics.AddRange(recomputed);
        await db.SaveChangesAsync(cancellationToken);

        return recomputed.Count;
    }

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
