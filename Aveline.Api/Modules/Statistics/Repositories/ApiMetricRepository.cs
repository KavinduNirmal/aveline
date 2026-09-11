using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>
/// The incremental rollup upsert (domain-model.md §7.1). On PostgreSQL it is a single
/// <c>INSERT ... ON CONFLICT ... DO UPDATE</c> against the NULLS NOT DISTINCT dimension
/// index. The in-memory provider used by tests has no conflict target, so it falls back to
/// a read-modify-write that produces the same result.
/// </summary>
public sealed class ApiMetricRepository(AppDbContext db) : IApiMetricRepository
{
    private const string UpsertSql =
        """
        INSERT INTO "ApiRequestMetrics"
            ("OrganizationId", "ApiKeyId", "UserId", "RouteTemplate", "HttpMethod",
             "StatusCode", "StatusClass", "IsThrottled", "WindowStart", "WindowSize",
             "RequestCount", "ErrorCount", "TotalDurationMs", "MaxDurationMs",
             "BucketCounts", "RequestBytes", "ResponseBytes")
        VALUES
            (@organizationId, @apiKeyId, @userId, @routeTemplate, @httpMethod,
             @statusCode, @statusClass, @isThrottled, @windowStart, @windowSize,
             @requestCount, @errorCount, @totalDurationMs, @maxDurationMs,
             @bucketCounts, @requestBytes, @responseBytes)
        ON CONFLICT ("OrganizationId", "ApiKeyId", "UserId", "RouteTemplate", "HttpMethod",
                     "StatusCode", "WindowStart")
        DO UPDATE SET
            "RequestCount" = "ApiRequestMetrics"."RequestCount" + EXCLUDED."RequestCount",
            "ErrorCount" = "ApiRequestMetrics"."ErrorCount" + EXCLUDED."ErrorCount",
            "TotalDurationMs" = "ApiRequestMetrics"."TotalDurationMs" + EXCLUDED."TotalDurationMs",
            "MaxDurationMs" = GREATEST("ApiRequestMetrics"."MaxDurationMs", EXCLUDED."MaxDurationMs"),
            "BucketCounts" = ARRAY(
                SELECT "ApiRequestMetrics"."BucketCounts"[g] + EXCLUDED."BucketCounts"[g]
                FROM generate_series(1, 12) AS g),
            "RequestBytes" = "ApiRequestMetrics"."RequestBytes" + EXCLUDED."RequestBytes",
            "ResponseBytes" = "ApiRequestMetrics"."ResponseBytes" + EXCLUDED."ResponseBytes";
        """;

    public async Task UpsertAsync(
        IReadOnlyList<ApiRequestMetric> metrics, CancellationToken cancellationToken = default)
    {
        if (metrics.Count == 0)
        {
            return;
        }

        if (!db.Database.IsRelational())
        {
            await UpsertInMemoryAsync(metrics, cancellationToken);
            return;
        }

        foreach (var metric in metrics)
        {
            await db.Database.ExecuteSqlRawAsync(UpsertSql, Parameters(metric), cancellationToken);
        }
    }

    private async Task UpsertInMemoryAsync(
        IReadOnlyList<ApiRequestMetric> metrics, CancellationToken cancellationToken)
    {
        foreach (var metric in metrics)
        {
            var existing = await db.ApiRequestMetrics.FirstOrDefaultAsync(
                row => row.OrganizationId == metric.OrganizationId
                       && row.ApiKeyId == metric.ApiKeyId
                       && row.UserId == metric.UserId
                       && row.RouteTemplate == metric.RouteTemplate
                       && row.HttpMethod == metric.HttpMethod
                       && row.StatusCode == metric.StatusCode
                       && row.WindowStart == metric.WindowStart,
                cancellationToken);

            if (existing is null)
            {
                db.ApiRequestMetrics.Add(metric);
                continue;
            }

            existing.RequestCount += metric.RequestCount;
            existing.ErrorCount += metric.ErrorCount;
            existing.TotalDurationMs += metric.TotalDurationMs;
            existing.MaxDurationMs = Math.Max(existing.MaxDurationMs, metric.MaxDurationMs);
            existing.RequestBytes += metric.RequestBytes;
            existing.ResponseBytes += metric.ResponseBytes;

            for (var i = 0; i < existing.BucketCounts.Length && i < metric.BucketCounts.Length; i++)
            {
                existing.BucketCounts[i] += metric.BucketCounts[i];
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static NpgsqlParameter[] Parameters(ApiRequestMetric metric) =>
    [
        Uuid("organizationId", metric.OrganizationId),
        Uuid("apiKeyId", metric.ApiKeyId),
        Uuid("userId", metric.UserId),
        new("routeTemplate", NpgsqlDbType.Varchar) { Value = metric.RouteTemplate },
        new("httpMethod", NpgsqlDbType.Varchar) { Value = metric.HttpMethod },
        new("statusCode", NpgsqlDbType.Smallint) { Value = metric.StatusCode },
        new("statusClass", NpgsqlDbType.Varchar) { Value = metric.StatusClass },
        new("isThrottled", NpgsqlDbType.Boolean) { Value = metric.IsThrottled },
        new("windowStart", NpgsqlDbType.TimestampTz) { Value = metric.WindowStart },
        new("windowSize", NpgsqlDbType.Varchar) { Value = metric.WindowSize },
        new("requestCount", NpgsqlDbType.Bigint) { Value = metric.RequestCount },
        new("errorCount", NpgsqlDbType.Bigint) { Value = metric.ErrorCount },
        new("totalDurationMs", NpgsqlDbType.Bigint) { Value = metric.TotalDurationMs },
        new("maxDurationMs", NpgsqlDbType.Integer) { Value = metric.MaxDurationMs },
        new("bucketCounts", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = metric.BucketCounts },
        new("requestBytes", NpgsqlDbType.Bigint) { Value = metric.RequestBytes },
        new("responseBytes", NpgsqlDbType.Bigint) { Value = metric.ResponseBytes },
    ];

    private static NpgsqlParameter Uuid(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = (object?)value ?? DBNull.Value };
}
