namespace Aveline.Api.Modules.Revenue.Services;

/// <summary>The validated window a revenue read was computed over, echoed to the client.</summary>
public sealed record RevenueWindowValue(
    DateTime From,
    DateTime To,
    string Granularity,
    int BucketCount,
    string TimeZone = "UTC");

/// <summary>
/// Window and granularity validation for the revenue family (S-52…S-55).
/// </summary>
/// <remarks>
/// Every failure is a `400` with `{ message }`, the convention set by `ApiStatisticsValidation` and
/// followed by the business-KPI family. The one rule that is deliberately *not* a clamp: a window
/// longer than <see cref="RevenueOptions.MaxWindowDays"/> is rejected **naming the effective
/// limit**, because silently shortening it would make the response describe a different period than
/// the caller asked for.
/// </remarks>
public static class RevenueWindowValidation
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    /// <summary>Default window length when the caller omits <c>from</c> and <c>to</c>.</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>Safety cap on the number of dense buckets materialised in memory.</summary>
    public const int MaxBuckets = 1200;

    /// <summary>Resolves a caller's window, rejecting one that is inverted or over the cap.</summary>
    public static bool TryCreate(
        RevenueOptions options,
        DateTime from,
        DateTime to,
        string granularity,
        out RevenueWindowValue? window,
        out string? message)
    {
        window = null;

        if (!TryParseGranularity(granularity, out var resolved, out message))
        {
            return false;
        }

        if (from >= to)
        {
            message = "'from' must be earlier than 'to'.";
            return false;
        }

        var span = (to - from).TotalDays;
        if (span > options.MaxWindowDays)
        {
            // The effective limit is named so the caller can correct the request rather than guess.
            message = $"The window cannot exceed {options.MaxWindowDays} days.";
            return false;
        }

        var bucketCount = CountBuckets(from, to, resolved);
        if (bucketCount > MaxBuckets)
        {
            message = $"The window produces {bucketCount} buckets, more than the {MaxBuckets} supported. "
                + "Use a coarser granularity.";
            return false;
        }

        window = new RevenueWindowValue(from, to, resolved, bucketCount);
        message = null;
        return true;
    }

    /// <summary>
    /// Resolves the window from raw query input, defaulting `to` to now and `from` to the configured
    /// default length. <paramref name="timeProvider"/> supplies "now" so the default is testable.
    /// </summary>
    public static bool TryResolve(
        RevenueOptions options,
        DateTime? from,
        DateTime? to,
        string? granularity,
        TimeProvider timeProvider,
        out RevenueWindowValue? window,
        out string? message)
    {
        var end = to ?? timeProvider.GetUtcNow().UtcDateTime;
        var start = from ?? end.AddDays(-DefaultWindowDays);
        var resolved = string.IsNullOrWhiteSpace(granularity) ? Day : granularity;

        return TryCreate(options, start, end, resolved, out window, out message);
    }

    /// <summary>Validates the caller's granularity, rejecting anything else by name.</summary>
    public static bool TryParseGranularity(string? value, out string granularity, out string? message)
    {
        granularity = (value ?? Day).Trim().ToLowerInvariant();
        if (granularity is Day or Week or Month)
        {
            message = null;
            return true;
        }

        message = $"granularity must be one of {Day}, {Week} or {Month}.";
        return false;
    }

    /// <summary>
    /// The number of buckets in the window. Every bucket the window *covers* is counted, including
    /// empty ones: the axis is dense, so a period with no rows is a real zero rather than a gap.
    /// </summary>
    private static int CountBuckets(DateTime from, DateTime to, string granularity) => granularity switch
    {
        Month => MonthsBetween(from, to),
        Week => Ceil((to - from).TotalDays / 7d),
        _ => Ceil((to - from).TotalDays),
    };

    private static int MonthsBetween(DateTime from, DateTime to)
    {
        var months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
        // A window that ends part-way through a month still covers that month.
        if (to.Day > from.Day || (to.Day == from.Day && to.TimeOfDay > from.TimeOfDay))
        {
            months++;
        }
        return Math.Max(1, months);
    }

    private static int Ceil(double value) => Math.Max(1, (int)Math.Ceiling(value));
}
