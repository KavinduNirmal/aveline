using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>
/// The incremental, idempotent write path for the hourly API rollup. The upsert is
/// additive, so a replayed batch cannot double-count beyond what the producer sent.
/// </summary>
public interface IApiMetricRepository
{
    Task UpsertAsync(IReadOnlyList<ApiRequestMetric> metrics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the rollup rows matching the filter. Org scoping is explicit because there is
    /// no EF global tenant filter (constraint C-3).
    /// </summary>
    Task<IReadOnlyList<ApiRequestMetric>> QueryAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);
}
