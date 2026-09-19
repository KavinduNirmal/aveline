using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Hourly at :05 UTC, recomputes and replaces the just-closed hour's rollup rows from the
/// rows the telemetry writer produced. Rewriting rather than adding makes it idempotent
/// (BR-6.10): derived fields are normalised and any duplicate dimension row is merged, so a
/// re-run leaves the same totals.
/// </summary>
/// <remarks>
/// Hour→day compaction <b>is</b> produced: when the just-closed hour is 23:00 UTC,
/// <see cref="RunAsync"/> also recomputes the day row for that date, which is what backs the
/// catalog's 400-day daily rollup and <c>Telemetry:DailyRollupRetentionDays</c>. The hourly and
/// daily windows share one table and one dimension index, distinguished by
/// <c>WindowSize</c>, so the day row does not collide with the 00:00 hour row.
/// </remarks>
public sealed class ApiStatsRollupJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<ApiStatsRollupJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "api-stats-rollup";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override TimeSpan? InitialDelay => NextHourlyRunAt(DateTime.UtcNow, minuteUtc: 5);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var closedHour = TruncateToHour(DateTime.UtcNow).AddHours(-1);
        var count = await RecomputeHourAsync(db, closedHour, cancellationToken);
        if (closedHour.Hour == 23)
        {
            await RecomputeDayAsync(db, closedHour.Date, cancellationToken);
        }
        return count;
    }

    /// <summary>Recompute-and-replace one hour. Public so tests and repairs can drive it.</summary>
    public static async Task<int> RecomputeHourAsync(
        AppDbContext db, DateTime hourStart, CancellationToken cancellationToken = default)
    {
        var start = TruncateToHour(hourStart);
        var end = start.AddHours(1);

        var rows = await db.ApiRequestMetrics
            .Where(row => row.WindowSize == "hour" && row.WindowStart >= start && row.WindowStart < end)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        var recomputed = rows
            .GroupBy(row => new
            {
                row.OrganizationId,
                row.ApiKeyId,
                row.UserId,
                row.RouteTemplate,
                row.HttpMethod,
                row.StatusCode,
            })
            .Select(group => Rebuild(group.Key.OrganizationId, group.Key.ApiKeyId, group.Key.UserId,
                group.Key.RouteTemplate, group.Key.HttpMethod, group.Key.StatusCode, start, group, "hour"))
            .ToList();

        // Delete and insert in two statements so the unique dimension index cannot trip on
        // a transient duplicate during a single batch.
        db.ApiRequestMetrics.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        db.ApiRequestMetrics.AddRange(recomputed);
        await db.SaveChangesAsync(cancellationToken);

        return recomputed.Count;
    }

    /// <summary>Recomputes the 24 hour rows for a closed day into daily ("day") summary rows.</summary>
    public static async Task<int> RecomputeDayAsync(
        AppDbContext db, DateTime dayStart, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(dayStart.Year, dayStart.Month, dayStart.Day, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var hourRows = await db.ApiRequestMetrics
            .Where(row => row.WindowSize == "hour" && row.WindowStart >= start && row.WindowStart < end)
            .ToListAsync(cancellationToken);

        if (hourRows.Count == 0)
        {
            return 0;
        }

        var existingDayRows = await db.ApiRequestMetrics
            .Where(row => row.WindowSize == "day" && row.WindowStart == start)
            .ToListAsync(cancellationToken);

        var recomputed = hourRows
            .GroupBy(row => new
            {
                row.OrganizationId,
                row.ApiKeyId,
                row.UserId,
                row.RouteTemplate,
                row.HttpMethod,
                row.StatusCode,
            })
            .Select(group => Rebuild(group.Key.OrganizationId, group.Key.ApiKeyId, group.Key.UserId,
                group.Key.RouteTemplate, group.Key.HttpMethod, group.Key.StatusCode, start, group, "day"))
            .ToList();

        if (existingDayRows.Count > 0)
        {
            db.ApiRequestMetrics.RemoveRange(existingDayRows);
            await db.SaveChangesAsync(cancellationToken);
        }

        db.ApiRequestMetrics.AddRange(recomputed);
        await db.SaveChangesAsync(cancellationToken);

        return recomputed.Count;
    }

    private static ApiRequestMetric Rebuild(
        Guid? organizationId, Guid? apiKeyId, Guid? userId, string routeTemplate, string httpMethod,
        short statusCode, DateTime windowStart, IEnumerable<ApiRequestMetric> rows, string windowSize = "hour")
    {
        var buckets = new int[ApiRequestMetric.BucketCount];
        var list = rows.ToList();

        foreach (var row in list)
        {
            for (var i = 0; i < buckets.Length && i < row.BucketCounts.Length; i++)
            {
                buckets[i] += row.BucketCounts[i];
            }
        }

        var requestCount = list.Sum(row => row.RequestCount);

        // Repair cumulative counts: the last bucket is the total and they never decrease.
        var running = 0;
        for (var i = 0; i < buckets.Length; i++)
        {
            running = Math.Max(running, buckets[i]);
            buckets[i] = running;
        }

        if (buckets.Length > 0)
        {
            buckets[^1] = (int)Math.Min(requestCount, int.MaxValue);
        }

        return new ApiRequestMetric
        {
            OrganizationId = organizationId,
            ApiKeyId = apiKeyId,
            UserId = userId,
            RouteTemplate = routeTemplate,
            HttpMethod = httpMethod,
            StatusCode = statusCode,
            StatusClass = $"{statusCode / 100}xx",
            IsThrottled = statusCode == 429,
            WindowStart = windowStart,
            WindowSize = windowSize,
            RequestCount = requestCount,
            ErrorCount = list.Sum(row => row.StatusCode >= 400 ? row.RequestCount : 0),
            TotalDurationMs = list.Sum(row => row.TotalDurationMs),
            MaxDurationMs = list.Count == 0 ? 0 : list.Max(row => row.MaxDurationMs),
            BucketCounts = buckets,
            RequestBytes = list.Sum(row => row.RequestBytes),
            ResponseBytes = list.Sum(row => row.ResponseBytes),
        };
    }

    internal static DateTime TruncateToHour(DateTime value) => new(
        value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    /// <summary>Delay from <paramref name="now"/> to the next hour at the given minute.</summary>
    internal static TimeSpan NextHourlyRunAt(DateTime now, int minuteUtc)
    {
        var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, minuteUtc, 0, DateTimeKind.Utc);
        if (next <= now)
        {
            next = next.AddHours(1);
        }

        return next - now;
    }
}
