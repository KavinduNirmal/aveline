namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>
/// One hourly API-consumption rollup row per unique dimension tuple (FR-6.1–FR-6.6,
/// S-24…S-32). The unique dimension index is <c>NULLS NOT DISTINCT</c>, which makes the
/// incremental <c>ON CONFLICT ... DO UPDATE</c> upsert correct without a surrogate key.
/// </summary>
/// <remarks>
/// The primary key is a <c>bigint</c> identity because this is the highest-volume table in
/// the system and the key is never exposed to clients (domain-model.md §7.1, C-4).
/// </remarks>
public class ApiRequestMetric
{
    /// <summary>Number of cumulative latency buckets, including the overflow bucket.</summary>
    public const int BucketCount = 12;

    public long Id { get; set; }

    /// <summary><c>NULL</c> = unattributed; counted in system statistics only (BR-6.1).</summary>
    public Guid? OrganizationId { get; set; }

    public Guid? ApiKeyId { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>Route template only — never the raw path (FR-6.2).</summary>
    public string RouteTemplate { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>Exact status so 429 is distinguishable (FR-6.8).</summary>
    public short StatusCode { get; set; }

    /// <summary><c>1xx</c>–<c>5xx</c>.</summary>
    public string StatusClass { get; set; } = string.Empty;

    /// <summary><c>true</c> iff <see cref="StatusCode"/> is 429.</summary>
    public bool IsThrottled { get; set; }

    /// <summary>Truncated to the hour, UTC.</summary>
    public DateTime WindowStart { get; set; }

    /// <summary><c>hour</c> or <c>day</c> (the same table holds both, S-24).</summary>
    public string WindowSize { get; set; } = "hour";

    public long RequestCount { get; set; }

    /// <summary>Requests with <see cref="StatusCode"/> &gt;= 400.</summary>
    public long ErrorCount { get; set; }

    /// <summary>Sum of durations, for the mean.</summary>
    public long TotalDurationMs { get; set; }

    public int MaxDurationMs { get; set; }

    /// <summary>
    /// 12 cumulative <c>le</c> bucket counts for
    /// <c>[5,10,25,50,100,250,500,1000,2500,5000,10000]</c> plus overflow.
    /// <c>BucketCounts[11] == RequestCount</c>.
    /// </summary>
    public int[] BucketCounts { get; set; } = new int[BucketCount];

    public long RequestBytes { get; set; }

    public long ResponseBytes { get; set; }
}
