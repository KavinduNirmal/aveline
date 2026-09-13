namespace Aveline.Api.Modules.Statistics.DTOs;

/// <summary>A page of results in the <c>{ items, page, pageSize, total }</c> envelope.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>
/// Data-quality envelope on every API statistics response. Unlike agent statistics these
/// signals are instrumented, so the flags default to <c>true</c>; the point of surfacing
/// them is that the frontend never reads a missing measurement as a zero.
/// </summary>
public sealed record ApiStatisticsDataQualityDto(
    bool RollupComplete,
    bool RawLogSampled,
    bool LatencyBuckets)
{
    public static ApiStatisticsDataQualityDto Default { get; } = new(true, true, true);
}

/// <summary>S-24 <c>apiRequestCount</c>.</summary>
public sealed record ApiRequestCountDto(
    DateTime From,
    DateTime To,
    long RequestCount,
    long SuccessCount,
    long ErrorCount,
    long ClientErrorCount,
    long ServerErrorCount,
    long ThrottledCount,
    ApiStatisticsDataQualityDto DataQuality);

public sealed record ApiErrorBreakdownItemDto(
    string? RouteTemplate,
    short? StatusCode,
    long RequestCount);

/// <summary>S-25 <c>apiErrorRate</c>; throttling is separated from errors (FR-6.8).</summary>
public sealed record ApiErrorRateDto(
    DateTime From,
    DateTime To,
    long RequestCount,
    double? ClientErrorRate,
    double? ServerErrorRate,
    double? ThrottleRate,
    IReadOnlyList<ApiErrorBreakdownItemDto> ByStatusCode,
    IReadOnlyList<ApiErrorBreakdownItemDto> ByRoute,
    ApiStatisticsDataQualityDto DataQuality);

public sealed record ApiLatencyPointDto(
    DateTime WindowStart,
    long RequestCount,
    double? AvgMs,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms);

/// <summary>S-26 <c>apiLatency</c>. Percentiles are <c>null</c> with a reason below the floor.</summary>
public sealed record ApiLatencyDto(
    DateTime From,
    DateTime To,
    long RequestCount,
    double? AvgMs,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    int MaxDurationMs,
    string Precision,
    string? Reason,
    IReadOnlyList<ApiLatencyPointDto> Series,
    ApiStatisticsDataQualityDto DataQuality);

public sealed record ApiEndpointUsageItemDto(
    string RouteTemplate,
    string HttpMethod,
    long RequestCount,
    long ErrorCount,
    long ThrottledCount,
    double AvgMs);

/// <summary>S-27 <c>apiEndpointUsage</c>.</summary>
public sealed record ApiEndpointUsageDto(
    IReadOnlyList<ApiEndpointUsageItemDto> Items,
    ApiStatisticsDataQualityDto DataQuality);

public sealed record ApiUserUsageItemDto(
    Guid? UserId,
    long RequestCount,
    long ErrorCount,
    double? ErrorRate,
    DateTime? LastSeenAt);

/// <summary>S-29 <c>apiUserUsage</c>.</summary>
public sealed record ApiUserUsageDto(
    IReadOnlyList<ApiUserUsageItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    ApiStatisticsDataQualityDto DataQuality);

public sealed record ApiKeyUsageItemDto(
    Guid? ApiKeyId,
    long RequestCount,
    long ErrorCount,
    double? ErrorRate,
    double AvgMs,
    string? TopEndpoint,
    DateTime? LastUsedAt);

/// <summary>S-28 <c>apiKeyUsage</c>.</summary>
public sealed record ApiKeyUsageDto(
    IReadOnlyList<ApiKeyUsageItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    ApiStatisticsDataQualityDto DataQuality);

public sealed record ApiQuotaStatusItemDto(
    string MetricKey,
    Guid? ApiKeyId,
    long? Limit,
    long Used,
    long? Remaining,
    double? PercentUsed,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime? WarnedAt,
    DateTime? ExhaustedAt);

/// <summary>S-30 <c>apiQuotaStatus</c>.</summary>
public sealed record ApiQuotaStatusDto(
    IReadOnlyList<ApiQuotaStatusItemDto> Items,
    ApiStatisticsDataQualityDto DataQuality);

public sealed record ApiSlowRequestItemDto(
    Guid? ApiKeyId,
    Guid? UserId,
    string RouteTemplate,
    string HttpMethod,
    short StatusCode,
    int DurationMs,
    DateTime OccurredAt,
    string? RequestId,
    Guid? TraceId,
    string? ResourceType,
    string? ResourceId,
    string? ErrorCode);

/// <summary>S-31 <c>apiSlowestRequests</c>; never carries request or response bodies.</summary>
public sealed record ApiSlowRequestsDto(
    IReadOnlyList<ApiSlowRequestItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    ApiStatisticsDataQualityDto DataQuality);

/// <summary>S-32 <c>apiBillableRequests</c>; excludes health, OpenAPI, preflight and 499s.</summary>
public sealed record ApiBillableRequestsDto(
    DateTime From,
    DateTime To,
    long BillableRequestCount,
    long ExcludedRequestCount,
    ApiStatisticsDataQualityDto DataQuality);
