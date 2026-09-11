using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>Insert-only access to the sampled raw request log (FR-6.4).</summary>
public interface IApiRequestLogRepository
{
    Task InsertAsync(IReadOnlyList<ApiRequestLog> logs, CancellationToken cancellationToken = default);
}
