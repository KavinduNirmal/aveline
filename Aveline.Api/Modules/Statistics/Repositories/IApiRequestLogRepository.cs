using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>Insert-only access to the sampled raw request log (FR-6.4).</summary>
public interface IApiRequestLogRepository
{
    Task InsertAsync(IReadOnlyList<ApiRequestLog> logs, CancellationToken cancellationToken = default);

    /// <summary>Slowest requests first, for S-31 (never returns bodies or raw IPs).</summary>
    Task<IReadOnlyList<ApiRequestLog>> ListSlowRequestsAsync(
        Guid? organizationId,
        DateTime from,
        DateTime to,
        int minDurationMs,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Org-scoped forensic read over the raw log window.</summary>
    Task<IReadOnlyList<ApiRequestLog>> QueryAsync(
        ApiStatisticsFilter filter, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts billable raw requests, excluding health, OpenAPI, <c>OPTIONS</c> and 499s
    /// (FR-6.9). The service prefers the exact rollup; this is the forensic cross-check.
    /// </summary>
    Task<long> CountBillableAsync(
        Guid? organizationId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
