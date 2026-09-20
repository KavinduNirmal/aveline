namespace Aveline.Api.Modules.Analytics;

/// <summary>
/// Configuration for the admin business-KPI endpoints. Deliberately separate from
/// <c>TelemetryOptions</c>: telemetry's window cap (92 days) and retention (7-day raw, 90-day
/// hourly) are tuned for request forensics, not for growth reporting over a year.
/// </summary>
public sealed class BusinessAnalyticsOptions
{
    public const string SectionName = "BusinessAnalytics";

    /// <summary>Maximum window in days; a longer window is a 400.</summary>
    public int MaxWindowDays { get; set; } = 400;

    /// <summary>Maximum trailing window for the DAU/WAU/MAU reading, in days.</summary>
    public int MaxRollingWindowDays { get; set; } = 30;

    /// <summary>Result cache TTL in seconds for the console endpoints.</summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>
    /// Longer TTL for the platform-wide gauges <c>SystemMetricCollector</c> reads. The collector
    /// ticks every 30 s by default, so an expensive distinct-count must not run at that cadence.
    /// One hour makes it at most one execution per hour per cluster when the value is shared
    /// through Redis.
    /// </summary>
    public int MetricCacheSeconds { get; set; } = 3600;

    /// <summary>
    /// When true, startup fails unless <c>IDistributedCache</c> is Redis-backed. The deployment
    /// runs two replicas, so this should be true there: without a shared cache the two replicas
    /// can serve different figures for the same request.
    /// </summary>
    public bool RequireSharedCache { get; set; }

    /// <summary>Maximum rows a ranking endpoint may return.</summary>
    public int MaxRankingLimit { get; set; } = 100;

    /// <summary>Highest <c>limit</c> a caller may ask for before it is rejected as absurd.</summary>
    public int HardRankingLimit { get; set; } = 1000;

    /// <summary>Default granularity when the caller omits one.</summary>
    public string DefaultGranularity { get; set; } = "day";
}
