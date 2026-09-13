using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Computes S-24…S-32 from the hourly rollup (exact, never sampled) and the sampled raw log
/// for S-31. Percentiles are interpolated from the cumulative buckets (BR-6.8).
/// </summary>
public sealed class ApiStatisticsService(
    IApiMetricRepository metrics,
    IApiRequestLogRepository logs,
    AppDbContext db,
    IOptions<TelemetryOptions> options) : IApiStatisticsService
{
    /// <summary>Route prefixes excluded from billing (FR-6.9).</summary>
    public static readonly string[] NonBillablePathPrefixes = ["/health", "/openapi"];

    private readonly TelemetryOptions _options = options.Value;

    public async Task<ApiRequestCountDto> GetRequestsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await metrics.QueryAsync(filter, cancellationToken);

        var total = rows.Sum(r => r.RequestCount);
        var clientErrors = rows.Where(r => r.StatusCode is >= 400 and < 500 && !r.IsThrottled).Sum(r => r.RequestCount);
        var serverErrors = rows.Where(r => r.StatusCode >= 500).Sum(r => r.RequestCount);
        var throttled = rows.Where(r => r.IsThrottled).Sum(r => r.RequestCount);
        var errorCount = rows.Where(r => r.StatusCode >= 400).Sum(r => r.RequestCount);

        return new ApiRequestCountDto(
            filter.From, filter.To, total,
            total - errorCount,
            errorCount,
            clientErrors, serverErrors, throttled,
            ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiErrorRateDto> GetErrorsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await metrics.QueryAsync(filter, cancellationToken);

        var total = rows.Sum(r => r.RequestCount);
        var clientErrors = rows.Where(r => r.StatusCode is >= 400 and < 500 && !r.IsThrottled).Sum(r => r.RequestCount);
        var serverErrors = rows.Where(r => r.StatusCode >= 500).Sum(r => r.RequestCount);
        var throttled = rows.Where(r => r.IsThrottled).Sum(r => r.RequestCount);

        var byStatusCode = rows
            .Where(r => r.StatusCode >= 400)
            .GroupBy(r => r.StatusCode)
            .Select(g => new ApiErrorBreakdownItemDto(null, g.Key, g.Sum(r => r.RequestCount)))
            .OrderByDescending(i => i.RequestCount)
            .ThenBy(i => i.StatusCode)
            .ToArray();

        var byRoute = rows
            .Where(r => r.StatusCode >= 400)
            .GroupBy(r => r.RouteTemplate)
            .Select(g => new ApiErrorBreakdownItemDto(g.Key, null, g.Sum(r => r.RequestCount)))
            .OrderByDescending(i => i.RequestCount)
            .ThenBy(i => i.RouteTemplate, StringComparer.Ordinal)
            .ToArray();

        return new ApiErrorRateDto(
            filter.From, filter.To, total,
            Rate(clientErrors, total), Rate(serverErrors, total), Rate(throttled, total),
            byStatusCode, byRoute, ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiLatencyDto> GetLatencyAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await metrics.QueryAsync(filter, cancellationToken);

        var total = rows.Sum(r => r.RequestCount);
        var buckets = SumBuckets(rows);
        var overall = LatencyBuckets.Compute(buckets, total, _options.MinSampleForPercentile);
        var totalDuration = rows.Sum(r => r.TotalDurationMs);
        var maxDuration = rows.Count == 0 ? 0 : rows.Max(r => r.MaxDurationMs);

        var series = rows
            .GroupBy(r => WindowKey(r.WindowStart, filter.GroupBy))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var count = g.Sum(r => r.RequestCount);
                var groupBuckets = SumBuckets(g);
                var percentiles = LatencyBuckets.Compute(groupBuckets, count, _options.MinSampleForPercentile);
                var duration = g.Sum(r => r.TotalDurationMs);
                return new ApiLatencyPointDto(
                    g.Key, count,
                    count == 0 ? null : (double)duration / count,
                    percentiles.P50Ms, percentiles.P95Ms, percentiles.P99Ms);
            })
            .ToArray();

        return new ApiLatencyDto(
            filter.From, filter.To, total,
            total == 0 ? null : (double)totalDuration / total,
            overall.P50Ms, overall.P95Ms, overall.P99Ms,
            maxDuration, "bucket-interpolated", overall.Reason,
            series, ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiEndpointUsageDto> GetEndpointsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await metrics.QueryAsync(filter, cancellationToken);

        var items = rows
            .GroupBy(r => new { r.RouteTemplate, r.HttpMethod })
            .Select(g => new ApiEndpointUsageItemDto(
                g.Key.RouteTemplate,
                g.Key.HttpMethod,
                g.Sum(r => r.RequestCount),
                g.Sum(r => r.ErrorCount),
                g.Where(r => r.IsThrottled).Sum(r => r.RequestCount),
                g.Sum(r => r.RequestCount) == 0
                    ? 0
                    : (double)g.Sum(r => r.TotalDurationMs) / g.Sum(r => r.RequestCount)))
            .OrderByDescending(i => i.RequestCount)
            .ThenBy(i => i.RouteTemplate, StringComparer.Ordinal)
            .ThenBy(i => i.HttpMethod, StringComparer.Ordinal)
            .ToArray();

        return new ApiEndpointUsageDto(items, ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiUserUsageDto> GetUsersAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await metrics.QueryAsync(filter, cancellationToken);

        var grouped = rows
            .GroupBy(r => r.UserId)
            .Select(g => new ApiUserUsageItemDto(
                g.Key,
                g.Sum(r => r.RequestCount),
                g.Sum(r => r.ErrorCount),
                Rate(g.Sum(r => r.ErrorCount), g.Sum(r => r.RequestCount)),
                g.Max(r => r.WindowStart)))
            .OrderByDescending(i => i.RequestCount)
            .ThenBy(i => i.UserId)
            .ToArray();

        var page = Page(grouped, filter.Page, filter.PageSize);
        return new ApiUserUsageDto(
            page.Items, page.Page, page.PageSize, page.Total, ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiKeyUsageDto> GetApiKeysAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await metrics.QueryAsync(filter, cancellationToken);

        var keyIds = rows.Select(r => r.ApiKeyId).Where(id => id is not null).Distinct().ToArray();
        var lastUsed = await db.ApiKeys.AsNoTracking()
            .Where(key => keyIds.Contains(key.Id))
            .ToDictionaryAsync(key => key.Id, key => key.LastUsedAt, cancellationToken);

        var grouped = rows
            .Where(r => r.ApiKeyId is not null)
            .GroupBy(r => r.ApiKeyId)
            .Select(g =>
            {
                var count = g.Sum(r => r.RequestCount);
                var top = g
                    .GroupBy(r => r.RouteTemplate)
                    .OrderByDescending(r => r.Sum(m => m.RequestCount))
                    .ThenBy(r => r.Key, StringComparer.Ordinal)
                    .FirstOrDefault()?.Key;

                return new ApiKeyUsageItemDto(
                    g.Key,
                    count,
                    g.Sum(r => r.ErrorCount),
                    Rate(g.Sum(r => r.ErrorCount), count),
                    count == 0 ? 0 : (double)g.Sum(r => r.TotalDurationMs) / count,
                    top,
                    g.Key is { } id && lastUsed.TryGetValue(id, out var at) ? at : null);
            })
            .OrderByDescending(i => i.RequestCount)
            .ThenBy(i => i.ApiKeyId)
            .ToArray();

        var page = Page(grouped, filter.Page, filter.PageSize);
        return new ApiKeyUsageDto(
            page.Items, page.Page, page.PageSize, page.Total, ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiQuotaStatusDto> GetQuotaAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Durable period rows. #224 layers the live Redis counter and entitlement limit on
        // top of these; this read path is always safe to serve.
        var rows = await db.ApiQuotaUsage.AsNoTracking()
            .Where(usage => usage.OrganizationId == organizationId)
            .OrderByDescending(usage => usage.PeriodStart)
            .ThenBy(usage => usage.MetricKey)
            .Take(200)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(usage => new ApiQuotaStatusItemDto(
                usage.MetricKey,
                usage.ApiKeyId,
                usage.LimitValue,
                usage.UsedValue,
                usage.LimitValue is { } limit && limit > 0 ? Math.Max(0, limit - usage.UsedValue) : null,
                usage.LimitValue is { } l && l > 0 ? (double)usage.UsedValue / l : null,
                usage.PeriodStart,
                usage.PeriodEnd,
                usage.WarnedAt,
                usage.ExhaustedAt))
            .ToArray();

        return new ApiQuotaStatusDto(items, ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiSlowRequestsDto> GetSlowRequestsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        // Bounded raw-log scan; the page is applied in memory over the slowest 500.
        var rows = await logs.ListSlowRequestsAsync(
            filter.OrganizationId, filter.From, filter.To,
            _options.SlowRequestMs, 500, cancellationToken);

        var items = rows
            .Select(log => new ApiSlowRequestItemDto(
                log.ApiKeyId, log.UserId, log.RouteTemplate, log.HttpMethod, log.StatusCode,
                log.DurationMs, log.OccurredAt, log.RequestId, log.TraceId,
                log.ResourceType, log.ResourceId, log.ErrorCode))
            .ToArray();

        var page = Page(items, filter.Page, filter.PageSize);
        return new ApiSlowRequestsDto(
            page.Items, page.Page, page.PageSize, page.Total, ApiStatisticsDataQualityDto.Default);
    }

    public async Task<ApiBillableRequestsDto> GetBillableAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await metrics.QueryAsync(filter, cancellationToken);

        var total = rows.Sum(r => r.RequestCount);
        var billable = rows
            .Where(r => r.HttpMethod != "OPTIONS" && r.StatusCode != 499)
            .Where(r => !NonBillablePathPrefixes.Any(prefix => r.RouteTemplate.StartsWith(prefix)))
            .Sum(r => r.RequestCount);

        return new ApiBillableRequestsDto(
            filter.From, filter.To, billable, total - billable, ApiStatisticsDataQualityDto.Default);
    }

    private static int[] SumBuckets(IEnumerable<ApiRequestMetric> rows)
    {
        var buckets = new int[LatencyBuckets.Count];
        foreach (var row in rows)
        {
            for (var i = 0; i < buckets.Length && i < row.BucketCounts.Length; i++)
            {
                buckets[i] += row.BucketCounts[i];
            }
        }

        return buckets;
    }

    private static DateTime WindowKey(DateTime windowStart, string? groupBy) => groupBy switch
    {
        "day" => DateTime.SpecifyKind(windowStart.Date, DateTimeKind.Utc),
        "month" => new DateTime(windowStart.Year, windowStart.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        _ => windowStart,
    };

    private static double? Rate(long numerator, long denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;

    private static PagedResult<T> Page<T>(IReadOnlyList<T> items, int page, int pageSize)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedSize = Math.Clamp(pageSize, 1, 200);
        var pageItems = items
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToArray();

        return new PagedResult<T>(pageItems, normalizedPage, normalizedSize, items.Count);
    }
}
