namespace Aveline.Api.Modules.Statistics.Domain;

/// <summary>
/// Validation for the API statistics query string (BR-6.9). Windows are UTC; a window longer
/// than <c>Telemetry:MaxWindowDays</c> or reversed is rejected so endpoints can return 400
/// instead of failing inside the query.
/// </summary>
public static class ApiStatisticsValidation
{
    public const int DefaultWindowDays = 30;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const string DefaultGroupBy = "hour";

    public static readonly string[] AllowedGroupBy = ["hour", "day", "month"];
    public static readonly string[] AllowedStatusClasses = ["1xx", "2xx", "3xx", "4xx", "5xx"];

    public static bool TryCreateWindow(
        DateTime? from,
        DateTime? to,
        int maxWindowDays,
        DateTime now,
        out DateTime start,
        out DateTime end,
        out string? error)
    {
        end = to ?? now;
        start = from ?? end.AddDays(-DefaultWindowDays);

        if (start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc)
        {
            error = "The 'from' and 'to' values must be UTC ISO-8601 timestamps (with a trailing 'Z').";
            return false;
        }

        if (end <= start)
        {
            error = "The 'to' value must be after 'from'.";
            return false;
        }

        if ((end - start).TotalDays > maxWindowDays)
        {
            error = $"The statistics window must be at most {maxWindowDays} days.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryParseStatusCode(string? value, out short statusCode, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            statusCode = 0;
            error = null;
            return true;
        }

        if (!short.TryParse(value, out statusCode) || statusCode is < 100 or > 599)
        {
            error = $"Unknown status '{value}'.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryParseStatusClass(string? value, out string? statusClass, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            statusClass = null;
            error = null;
            return true;
        }

        var normalized = value.ToLowerInvariant();
        if (!AllowedStatusClasses.Contains(normalized))
        {
            statusClass = null;
            error = $"Unknown statusClass '{value}'.";
            return false;
        }

        statusClass = normalized;
        error = null;
        return true;
    }

    public static bool TryParseGroupBy(string? value, out string groupBy, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            groupBy = DefaultGroupBy;
            error = null;
            return true;
        }

        var normalized = value.ToLowerInvariant();
        if (!AllowedGroupBy.Contains(normalized))
        {
            groupBy = DefaultGroupBy;
            error = $"Unknown groupBy '{value}'.";
            return false;
        }

        groupBy = normalized;
        error = null;
        return true;
    }

    public static (int Page, int PageSize) NormalizePaging(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
