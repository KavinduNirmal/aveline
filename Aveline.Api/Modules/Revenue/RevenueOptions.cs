namespace Aveline.Api.Modules.Revenue;

/// <summary>
/// Configuration for the revenue reads (S-50…S-55).
/// </summary>
/// <remarks>
/// Separate from <c>BusinessAnalyticsOptions</c> even though the defaults match, because the two
/// families answer different questions and a tuning change to one should not silently move the
/// other's window.
/// </remarks>
public sealed class RevenueOptions
{
    public const string SectionName = "Revenue";

    /// <summary>
    /// Longest window a revenue read accepts. Defaults to **400 days**, matching the retention the
    /// S-catalog claims for the ledger, rather than telemetry's 92-day forensic cap.
    /// </summary>
    public int MaxWindowDays { get; set; } = 400;

    /// <summary>Console cache TTL for the statistics reads.</summary>
    public int CacheSeconds { get; set; } = 60;
}
