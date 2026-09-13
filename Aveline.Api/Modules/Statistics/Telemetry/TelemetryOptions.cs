namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Configuration for the request telemetry pipeline (implementation-plan.md §10.2).
/// Safe defaults mean telemetry is on but bounded and cheap; every value is overridable.
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Kill switch: when <c>false</c> the middleware is a no-op.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Bounded channel capacity; when full the oldest sample is dropped (BR-6.4).</summary>
    public int BufferCapacity { get; set; } = 10_000;

    public string[] ExcludedPaths { get; set; } =
        ["/health", "/health/live", "/health/ready", "/openapi"];

    /// <summary>Requests slower than this are always written to the raw log.</summary>
    public int SlowRequestMs { get; set; } = 1000;

    /// <summary>Fraction of remaining 2xx requests written to the raw log.</summary>
    public double SuccessSampleRate { get; set; } = 0.1;

    /// <summary>Salt prepended to the client IP before hashing.</summary>
    public string IpHashSalt { get; set; } = string.Empty;

    /// <summary>Maximum samples drained in one writer batch.</summary>
    public int WriterBatchSize { get; set; } = 500;

    /// <summary>Maximum idle wait before the writer flushes what it has.</summary>
    public int WriterFlushSeconds { get; set; } = 2;

    /// <summary>Below this many samples, percentiles are <c>null</c> (BR-6.8).</summary>
    public int MinSampleForPercentile { get; set; } = 20;

    /// <summary>Maximum statistics window in days; a longer window is a 400 (BR-6.9).</summary>
    public int MaxWindowDays { get; set; } = 92;

    /// <summary>Usage percentage at which a quota warning is published.</summary>
    public int QuotaWarningPercent { get; set; } = 80;

    /// <summary>Raw request-log retention (FR-6.4).</summary>
    public int RawLogRetentionDays { get; set; } = 7;

    /// <summary>Hourly rollup retention; daily compaction beyond this is deferred.</summary>
    public int HourlyRollupRetentionDays { get; set; } = 90;

    /// <summary>Daily rollup retention (S-24); no day rows are produced yet.</summary>
    public int DailyRollupRetentionDays { get; set; } = 400;
}
