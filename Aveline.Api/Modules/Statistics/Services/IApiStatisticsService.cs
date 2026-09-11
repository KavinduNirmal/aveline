using Aveline.Api.Modules.Statistics.DTOs;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// The API consumption statistics catalog S-24…S-32 over a validated UTC window. All
/// aggregation is computed from the hourly rollup plus the sampled raw log for slow
/// requests; there is no separate daily table (statistics-catalog.md §9).
/// </summary>
public interface IApiStatisticsService
{
    Task<ApiRequestCountDto> GetRequestsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<ApiErrorRateDto> GetErrorsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<ApiLatencyDto> GetLatencyAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<ApiEndpointUsageDto> GetEndpointsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<ApiUserUsageDto> GetUsersAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<ApiKeyUsageDto> GetApiKeysAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<ApiQuotaStatusDto> GetQuotaAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    Task<ApiSlowRequestsDto> GetSlowRequestsAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<ApiBillableRequestsDto> GetBillableAsync(
        ApiStatisticsFilter filter, CancellationToken cancellationToken = default);
}
