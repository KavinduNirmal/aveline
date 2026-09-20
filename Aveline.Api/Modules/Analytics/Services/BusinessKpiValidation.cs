namespace Aveline.Api.Modules.Analytics.Services;

/// <summary>The usage measures an organization ranking may be sorted by (S-49).</summary>
public enum BusinessRankingMetric
{
    Messages,
    AgentRuns,
    ApiRequests,
    BlossomUnits,
}

/// <summary>
/// The raw query-string inputs shared by the six business-KPI endpoints. Bound here rather
/// than as six separate parameter lists so validation cannot drift between endpoints.
/// </summary>
public sealed record BusinessQueryParameters
{
    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public string? Granularity { get; init; }

    public Guid? OrganizationId { get; init; }

    public string? Metric { get; init; }

    public int? Limit { get; init; }
}

/// <summary>The validated window a series was computed over, echoed to the client.</summary>
public sealed record BusinessWindow(
    DateTime From,
    DateTime To,
    string Granularity,
    int BucketCount,
    string TimeZone = "UTC");

/// <summary>Outcome of a validation attempt: either a value and no message, or vice versa.</summary>
public readonly record struct BusinessValidation(bool IsValid, string? Message)
{
    public static BusinessValidation Valid() => new(true, null);

    public static BusinessValidation Invalid(string message) => new(false, message);
}

/// <summary>
/// Window, granularity, metric and limit validation for the business-KPI family. Every failure
/// is a 400 with <c>{ message }</c>, the convention set by <c>ApiStatisticsValidation</c>.
/// </summary>
public static class BusinessKpiValidation
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    /// <summary>Default window length when the caller omits <c>from</c> and <c>to</c>.</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>Safety cap on the number of dense buckets materialised in memory.</summary>
    public const int MaxBuckets = 1200;

    /// <summary>
    /// Validates and materialises the window. <paramref name="timeProvider"/> supplies "now" so
    /// the default <c>to</c> is testable.
    /// </summary>
    public static BusinessValidation TryCreateWindow(
        BusinessQueryParameters parameters,
        BusinessAnalyticsOptions options,
        TimeProvider timeProvider,
        out BusinessWindow? window,
        string? granularity = null)
    {
        window = null;

        string resolvedGranularity;
        if (granularity is not null)
        {
            resolvedGranularity = granularity;
        }
        else if (!TryParseGranularity(parameters.Granularity, options, out resolvedGranularity, out var error))
        {
            return BusinessValidation.Invalid(error!);
        }

        var to = parameters.To ?? timeProvider.GetUtcNow().UtcDateTime;
        var from = parameters.From ?? to.AddDays(-DefaultWindowDays);

        if (from >= to)
        {
            return BusinessValidation.Invalid("'from' must be earlier than 'to'.");
        }

        var span = (to - from).TotalDays;
        if (span > options.MaxWindowDays)
        {
            return BusinessValidation.Invalid(
                $"The window cannot exceed {options.MaxWindowDays} days.");
        }

        window = new BusinessWindow(from, to, resolvedGranularity, CountBuckets(from, to, resolvedGranularity));
        return BusinessValidation.Valid();
    }

    /// <summary>Validates the caller's granularity, falling back to the configured default.</summary>
    public static bool TryParseGranularity(
        string? value,
        BusinessAnalyticsOptions options,
        out string granularity,
        out string? error)
    {
        error = null;
        granularity = string.IsNullOrWhiteSpace(value)
            ? (string.IsNullOrWhiteSpace(options.DefaultGranularity) ? Day : options.DefaultGranularity.ToLowerInvariant())
            : value.Trim().ToLowerInvariant();

        if (granularity is Day or Week or Month)
        {
            return true;
        }

        error = "'granularity' must be one of: day, week, month.";
        granularity = Day;
        return false;
    }

    /// <summary>Validates the ranking metric, defaulting to <c>apiRequests</c>.</summary>
    public static bool TryParseRankingMetric(
        string? value,
        out BusinessRankingMetric metric,
        out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            metric = BusinessRankingMetric.ApiRequests;
            return true;
        }

        switch (value)
        {
            case "messages":
                metric = BusinessRankingMetric.Messages;
                return true;
            case "agentRuns":
                metric = BusinessRankingMetric.AgentRuns;
                return true;
            case "apiRequests":
                metric = BusinessRankingMetric.ApiRequests;
                return true;
            case "blossomUnits":
                metric = BusinessRankingMetric.BlossomUnits;
                return true;
            default:
                error = "'metric' must be one of: messages, agentRuns, apiRequests, blossomUnits.";
                metric = BusinessRankingMetric.ApiRequests;
                return false;
        }
    }

    /// <summary>
    /// Validates the ranking limit. A non-positive or absurd value is rejected; a value above
    /// the configured maximum but below the hard ceiling is clamped, so the endpoint never
    /// silently returns fewer rows than a caller can reasonably ask for.
    /// </summary>
    public static bool TryParseLimit(
        int? value,
        BusinessAnalyticsOptions options,
        out int limit,
        out string? error)
    {
        error = null;

        if (value is null)
        {
            limit = Math.Min(20, options.MaxRankingLimit);
            return true;
        }

        if (value <= 0)
        {
            error = "'limit' must be a positive integer.";
            limit = 0;
            return false;
        }

        if (value > options.HardRankingLimit)
        {
            error = $"'limit' cannot exceed {options.HardRankingLimit}.";
            limit = 0;
            return false;
        }

        limit = Math.Min(value.Value, options.MaxRankingLimit);
        return true;
    }

    /// <summary>
    /// The single bucketing helper, so granularity cannot drift between endpoints. Weeks are
    /// ISO weeks (Monday-anchored); months are calendar months.
    /// </summary>
    public static DateTime Truncate(DateTime value, string granularity) => granularity switch
    {
        Day => value.Date,
        Week => value.Date.AddDays(-(((int)value.DayOfWeek + 6) % 7)),
        Month => new DateTime(value.Year, value.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Unknown granularity."),
    };

    /// <summary>The next bucket boundary strictly after <paramref name="bucketStart"/>.</summary>
    public static DateTime Advance(DateTime bucketStart, string granularity) => granularity switch
    {
        Day => bucketStart.AddDays(1),
        Week => bucketStart.AddDays(7),
        Month => bucketStart.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Unknown granularity."),
    };

    /// <summary>
    /// The dense bucket axis between <paramref name="from"/> and <paramref name="to"/>: every
    /// bucket boundary in the half-open interval, capped so a pathological window cannot
    /// materialise an unbounded list.
    /// </summary>
    public static IReadOnlyList<DateTime> BuildAxis(DateTime from, DateTime to, string granularity)
    {
        var axis = new List<DateTime>();
        var cursor = Truncate(from, granularity);

        while (cursor < to && axis.Count < MaxBuckets)
        {
            axis.Add(cursor);
            cursor = Advance(cursor, granularity);
        }

        return axis;
    }

    /// <summary>
    /// The number of buckets on the dense axis between <paramref name="from"/> and
    /// <paramref name="to"/>. Public so a caller can reconstruct a <see cref="BusinessWindow"/>
    /// without duplicating the axis rule.
    /// </summary>
    public static int CountBuckets(DateTime from, DateTime to, string granularity)
    {
        var count = 0;
        var cursor = Truncate(from, granularity);

        while (cursor < to && count < MaxBuckets)
        {
            count++;
            cursor = Advance(cursor, granularity);
        }

        return Math.Max(count, 1);
    }
}
