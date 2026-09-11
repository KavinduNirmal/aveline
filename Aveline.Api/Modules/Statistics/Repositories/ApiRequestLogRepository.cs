using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>
/// EF implementation over the partition-pruned raw log. Queries always bound the
/// <c>OccurredAt</c> window so PostgreSQL can eliminate partitions.
/// </summary>
public sealed class ApiRequestLogRepository(AppDbContext db) : IApiRequestLogRepository
{
    /// <summary>Paths excluded from billing (FR-6.9).</summary>
    public static readonly string[] NonBillablePathPrefixes = ["/health", "/openapi"];

    public async Task InsertAsync(
        IReadOnlyList<ApiRequestLog> logs, CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
        {
            return;
        }

        db.ApiRequestLogs.AddRange(logs);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApiRequestLog>> ListSlowRequestsAsync(
        Guid? organizationId,
        DateTime from,
        DateTime to,
        int minDurationMs,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = db.ApiRequestLogs.AsNoTracking()
            .Where(log => log.OccurredAt >= from && log.OccurredAt <= to)
            .Where(log => log.DurationMs > minDurationMs);

        if (organizationId is { } org)
        {
            query = query.Where(log => log.OrganizationId == org);
        }

        return await query
            .OrderByDescending(log => log.DurationMs)
            .ThenByDescending(log => log.OccurredAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApiRequestLog>> QueryAsync(
        ApiStatisticsFilter filter, int limit, CancellationToken cancellationToken = default)
    {
        var query = db.ApiRequestLogs.AsNoTracking()
            .Where(log => log.OccurredAt >= filter.From && log.OccurredAt <= filter.To);

        if (filter.OrganizationId is { } organizationId)
        {
            query = query.Where(log => log.OrganizationId == organizationId);
        }

        if (filter.ApiKeyId is { } apiKeyId)
        {
            query = query.Where(log => log.ApiKeyId == apiKeyId);
        }

        if (filter.StatusCode is { } statusCode)
        {
            query = query.Where(log => log.StatusCode == statusCode);
        }

        if (!string.IsNullOrWhiteSpace(filter.RouteTemplate))
        {
            query = query.Where(log => log.RouteTemplate == filter.RouteTemplate);
        }

        if (!string.IsNullOrWhiteSpace(filter.HttpMethod))
        {
            var method = filter.HttpMethod.ToUpperInvariant();
            query = query.Where(log => log.HttpMethod == method);
        }

        return await query
            .OrderByDescending(log => log.OccurredAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
    }

    public async Task<long> CountBillableAsync(
        Guid? organizationId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var query = db.ApiRequestLogs.AsNoTracking()
            .Where(log => log.OccurredAt >= from && log.OccurredAt <= to)
            .Where(log => log.HttpMethod != "OPTIONS" && log.StatusCode != 499)
            .Where(log => !NonBillablePathPrefixes.Any(prefix => log.RouteTemplate.StartsWith(prefix)));

        if (organizationId is { } org)
        {
            query = query.Where(log => log.OrganizationId == org);
        }

        return await query.LongCountAsync(cancellationToken);
    }
}
