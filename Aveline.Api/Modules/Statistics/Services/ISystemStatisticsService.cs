using Aveline.Api.Modules.Statistics.DTOs;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Read model for the team-only system statistics endpoints (S-33–S-43, FR-7.13–FR-7.15).
/// Every composite response names the metrics it could not determine instead of returning a
/// misleading zero (BR-7.10).
/// </summary>
public interface ISystemStatisticsService
{
    Task<SystemOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<SystemMetricSeriesDto> GetMetricSeriesAsync(
        string metric, DateTime from, DateTime to, string? windowSize,
        CancellationToken cancellationToken = default);

    Task<QueueDepthDto> GetQueuesAsync(CancellationToken cancellationToken = default);

    Task<ErrorRateDto> GetErrorsAsync(
        DateTime from, DateTime to, string windowSize, CancellationToken cancellationToken = default);

    Task<ThroughputDto> GetThroughputAsync(
        DateTime from, DateTime to, string windowSize, CancellationToken cancellationToken = default);

    Task<EventBusStatsDto> GetEventBusAsync(
        DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);

    Task<SystemAlertPageDto> GetAlertsAsync(
        string? status, string? severity, Guid? ruleId, int page, int pageSize,
        CancellationToken cancellationToken = default);
}
