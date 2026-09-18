using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Jobs;

/// <summary>
/// Daily 01:30 UTC job that aggregates previous day's AiUsageRecords into DailyBillingMetrics.
/// Idempotent (delete-and-replace per day).
/// </summary>
public sealed class BillingRollupJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<BillingRollupJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = NextDailyRunAt(DateTime.UtcNow, hourUtc: 1, minuteUtc: 30);
        await Task.Delay(initialDelay, stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var handle = await jobLock.TryAcquireAsync("billing-stats-rollup", Interval, stoppingToken);
                if (handle is not null)
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var yesterday = DateTime.UtcNow.Date.AddDays(-1);
                    await RecomputeDayAsync(db, yesterday, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error occurred during BillingRollupJob execution.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    public static async Task<int> RecomputeDayAsync(
        AppDbContext db, DateTime day, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var usageRecords = await db.AiUsageRecords
            .Include(r => r.Organization)
            .Where(r => r.CreatedAt >= start && r.CreatedAt < end)
            .ToListAsync(cancellationToken);

        if (usageRecords.Count == 0)
        {
            return 0;
        }

        var existing = await db.DailyBillingMetrics
            .Where(m => m.Day == start)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            db.DailyBillingMetrics.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var recomputed = usageRecords
            .GroupBy(r => new
            {
                r.OrganizationId,
                PlanTier = r.Organization?.PlanTier ?? PlanTier.Seed,
                r.Provider,
                r.Model,
            })
            .Select(group =>
            {
                var list = group.ToList();
                return new DailyBillingMetric
                {
                    Id = Guid.CreateVersion7(),
                    OrganizationId = group.Key.OrganizationId,
                    Day = start,
                    PlanTier = group.Key.PlanTier,
                    Provider = group.Key.Provider,
                    Model = group.Key.Model,
                    RequestCount = list.Count,
                    InputTokens = list.Sum(r => (long)r.InputTokens),
                    OutputTokens = list.Sum(r => (long)r.OutputTokens),
                    CachedTokens = list.Sum(r => (long)r.CachedTokens),
                    ActualCostUsd = list.Sum(r => r.ActualCostUsd),
                    BlossomUnits = list.Sum(r => r.BlossomUnits),
                    CreatedAt = DateTime.UtcNow,
                };
            })
            .ToList();

        db.DailyBillingMetrics.AddRange(recomputed);
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
