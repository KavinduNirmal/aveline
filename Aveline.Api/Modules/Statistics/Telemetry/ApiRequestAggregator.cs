using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Folds a batch of <see cref="ApiRequestSample"/>s into the hourly rollup rows and the
/// sampled raw log rows (FR-6.1, BR-6.3). Pure, so it is unit-testable without a database.
/// </summary>
public static class ApiRequestAggregator
{
    /// <summary>Cumulative <c>le</c> bucket upper bounds in milliseconds (domain-model.md §7.1).</summary>
    public static int[] BucketBoundaries => LatencyBuckets.UpperBoundsMs;

    /// <summary>The index of the smallest bucket whose upper bound is &gt;= <paramref name="durationMs"/>.</summary>
    public static int BucketIndex(int durationMs) => LatencyBuckets.Assign(durationMs);

    public static (List<ApiRequestMetric> Metrics, List<ApiRequestLog> Logs) Aggregate(
        IReadOnlyList<ApiRequestSample> samples)
    {
        var metrics = new Dictionary<MetricKey, ApiRequestMetric>();
        var logs = new List<ApiRequestLog>();

        foreach (var sample in samples)
        {
            var windowStart = new DateTime(
                sample.OccurredAt.Year, sample.OccurredAt.Month, sample.OccurredAt.Day,
                sample.OccurredAt.Hour, 0, 0, DateTimeKind.Utc);

            var key = new MetricKey(
                sample.OrganizationId, sample.ApiKeyId, sample.UserId,
                sample.RouteTemplate, sample.HttpMethod, sample.StatusCode, windowStart);

            if (!metrics.TryGetValue(key, out var metric))
            {
                metric = new ApiRequestMetric
                {
                    OrganizationId = sample.OrganizationId,
                    ApiKeyId = sample.ApiKeyId,
                    UserId = sample.UserId,
                    RouteTemplate = sample.RouteTemplate,
                    HttpMethod = sample.HttpMethod,
                    StatusCode = sample.StatusCode,
                    StatusClass = StatusClass(sample.StatusCode),
                    IsThrottled = sample.StatusCode == 429,
                    WindowStart = windowStart,
                    WindowSize = "hour",
                    BucketCounts = new int[ApiRequestMetric.BucketCount],
                };
                metrics[key] = metric;
            }

            metric.RequestCount++;
            if (sample.StatusCode >= 400)
            {
                metric.ErrorCount++;
            }

            metric.TotalDurationMs += sample.DurationMs;
            metric.MaxDurationMs = Math.Max(metric.MaxDurationMs, sample.DurationMs);
            metric.RequestBytes += sample.RequestBytes;
            metric.ResponseBytes += sample.ResponseBytes;

            // Cumulative le semantics: a sample counts in its bucket and every wider bucket.
            for (var i = BucketIndex(sample.DurationMs); i < metric.BucketCounts.Length; i++)
            {
                metric.BucketCounts[i]++;
            }

            if (sample.ShouldPersistRaw)
            {
                logs.Add(new ApiRequestLog
                {
                    OccurredAt = sample.OccurredAt,
                    OrganizationId = sample.OrganizationId,
                    ApiKeyId = sample.ApiKeyId,
                    UserId = sample.UserId,
                    RouteTemplate = sample.RouteTemplate,
                    HttpMethod = sample.HttpMethod,
                    StatusCode = sample.StatusCode,
                    DurationMs = sample.DurationMs,
                    RequestBytes = sample.RequestBytes,
                    ResponseBytes = sample.ResponseBytes,
                    RequestId = sample.RequestId,
                    TraceId = sample.TraceId,
                    ClientIpHash = sample.ClientIpHash,
                    UserAgentHash = sample.UserAgentHash,
                    ErrorCode = sample.ErrorCode,
                    ResourceType = sample.ResourceType,
                    ResourceId = sample.ResourceId,
                });
            }
        }

        var ordered = metrics.Values
            .OrderBy(m => m.WindowStart)
            .ThenBy(m => m.OrganizationId)
            .ThenBy(m => m.RouteTemplate, StringComparer.Ordinal)
            .ThenBy(m => m.HttpMethod, StringComparer.Ordinal)
            .ThenBy(m => m.StatusCode)
            .ToList();

        return (ordered, logs);
    }

    public static string StatusClass(short statusCode) => $"{statusCode / 100}xx";

    private readonly record struct MetricKey(
        Guid? OrganizationId,
        Guid? ApiKeyId,
        Guid? UserId,
        string RouteTemplate,
        string HttpMethod,
        short StatusCode,
        DateTime WindowStart);
}
