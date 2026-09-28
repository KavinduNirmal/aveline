using Aveline.Api.Modules.Analytics;
using Aveline.Api.Modules.Analytics.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 1 of the Business KPIs plan (§5.4.2, §5.4.3): every rejection path of the shared
/// window/granularity/metric/limit contract, plus the boundary at exactly
/// <see cref="BusinessAnalyticsOptions.MaxWindowDays"/>.
/// </summary>
public class BusinessKpiValidationTests
{
    /// <summary>Deterministic clock; the production registration uses <see cref="TimeProvider.System"/>.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static TimeProvider Clock() => new FixedTimeProvider(Now);

    private static BusinessAnalyticsOptions Options(Action<BusinessAnalyticsOptions>? configure = null)
    {
        var options = new BusinessAnalyticsOptions();
        configure?.Invoke(options);
        return options;
    }

    // ── Window ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultsTheWindowToTheTrailingThirtyDays()
    {
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters(), Options(), Clock(), out var window);

        Assert.True(result.IsValid);
        Assert.NotNull(window);
        Assert.Equal(Now.UtcDateTime.AddDays(-30), window!.From);
        Assert.Equal(Now.UtcDateTime, window.To);
        // A dense axis over [from - 30d, to) starting at the truncated boundary: the leading
        // bucket is clipped by `from`, the trailing one is the open day. 31 buckets total.
        Assert.Equal(31, window.BucketCount);
        Assert.Equal("UTC", window.TimeZone);
    }

    [Fact]
    public void DefaultsTheToBoundToUtcNowWhenOnlyFromIsSupplied()
    {
        var from = Now.UtcDateTime.AddDays(-10);
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters { From = from }, Options(), Clock(), out var window);

        Assert.True(result.IsValid);
        Assert.Equal(from, window!.From);
        Assert.Equal(Now.UtcDateTime, window.To);
    }

    [Fact]
    public void RejectsAWindowWhereFromIsNotBeforeTo()
    {
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters { From = Now.UtcDateTime, To = Now.UtcDateTime },
            Options(), Clock(), out var window);

        Assert.False(result.IsValid);
        Assert.Null(window);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void RejectsAWindowLongerThanMaxWindowDays()
    {
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters
            {
                From = Now.UtcDateTime.AddDays(-401),
                To = Now.UtcDateTime,
            },
            Options(), Clock(), out var window);

        Assert.False(result.IsValid);
        Assert.Null(window);
        Assert.Contains("400", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsAWindowOfExactlyMaxWindowDays()
    {
        // The plan's stated boundary: (to - from).TotalDays <= MaxWindowDays is valid.
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters
            {
                From = Now.UtcDateTime.AddDays(-400),
                To = Now.UtcDateTime,
            },
            Options(), Clock(), out var window);

        Assert.True(result.IsValid);
        Assert.NotNull(window);
        Assert.Equal(401, window!.BucketCount);
    }

    [Fact]
    public void RejectsAWindowOneTickOverTheBoundary()
    {
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters
            {
                From = Now.UtcDateTime.AddDays(-400).AddSeconds(-1),
                To = Now.UtcDateTime,
            },
            Options(), Clock(), out var window);

        Assert.False(result.IsValid);
        Assert.Null(window);
    }

    [Fact]
    public void AWeekWindowCountsIsoMondayAlignedBucketsCoveringTheWindow()
    {
        // 20 Sep 2026 is a Sunday, so a 10-day window spans Monday 14 Sep..Sunday 20 Sep:
        // exactly two ISO weeks (14 Sep and 21 Sep is outside; 7 Sep is outside).
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters
            {
                From = Now.UtcDateTime.AddDays(-10),
                To = Now.UtcDateTime,
            },
            Options(), Clock(), out var window, granularity: "week");

        Assert.True(result.IsValid);
        Assert.Equal("week", window!.Granularity);
        Assert.Equal(2, window.BucketCount);
    }

    [Fact]
    public void AMonthWindowCountsCalendarMonthBucketsCoveringTheWindow()
    {
        var result = BusinessKpiValidation.TryCreateWindow(
            new BusinessQueryParameters
            {
                From = Now.UtcDateTime.AddDays(-90),
                To = Now.UtcDateTime,
            },
            Options(), Clock(), out var window, granularity: "month");

        Assert.True(result.IsValid);
        // 22 Jun..20 Sep 2026 spans four calendar months: Jun, Jul, Aug, Sep.
        Assert.Equal(4, window!.BucketCount);
    }

    // ── Granularity ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultsGranularityToDay()
    {
        Assert.True(BusinessKpiValidation.TryParseGranularity(
            null, Options(), out var granularity, out var error));
        Assert.Equal("day", granularity);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("day")]
    [InlineData("week")]
    [InlineData("month")]
    [InlineData("WEEK")]
    public void AcceptsTheThreeGranularitiesCaseInsensitively(string value)
    {
        Assert.True(BusinessKpiValidation.TryParseGranularity(
            value, Options(), out var granularity, out _));
        Assert.Equal(value.ToLowerInvariant(), granularity);
    }

    [Theory]
    [InlineData("year")]
    [InlineData("hour")]
    [InlineData("daily")]
    public void RejectsAnUnknownGranularity(string value)
    {
        Assert.False(BusinessKpiValidation.TryParseGranularity(
            value, Options(), out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("granularity", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyGranularityIsTreatedAsAbsentRatherThanInvalid()
    {
        // `?granularity=` binds as an empty string; a caller that supplied no value gets the
        // default rather than a 400.
        Assert.True(BusinessKpiValidation.TryParseGranularity(
            string.Empty, Options(), out var granularity, out _));
        Assert.Equal("day", granularity);
    }

    [Fact]
    public void UsesTheConfiguredDefaultGranularityWhenTheCallerOmitsOne()
    {
        Assert.True(BusinessKpiValidation.TryParseGranularity(
            null, Options(o => o.DefaultGranularity = "month"), out var granularity, out _));
        Assert.Equal("month", granularity);
    }

    // ── Ranking metric ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultsTheRankingMetricToApiRequests()
    {
        Assert.True(BusinessKpiValidation.TryParseRankingMetric(null, out var metric, out var error));
        Assert.Equal(BusinessRankingMetric.ApiRequests, metric);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("messages", BusinessRankingMetric.Messages)]
    [InlineData("agentRuns", BusinessRankingMetric.AgentRuns)]
    [InlineData("apiRequests", BusinessRankingMetric.ApiRequests)]
    [InlineData("blossomUnits", BusinessRankingMetric.BlossomUnits)]
    public void AcceptsTheFourRankingMetrics(string value, BusinessRankingMetric expected)
    {
        Assert.True(BusinessKpiValidation.TryParseRankingMetric(value, out var metric, out _));
        Assert.Equal(expected, metric);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("messages ")]
    [InlineData("MESSAGES")]
    public void RejectsAnUnknownRankingMetric(string value)
    {
        Assert.False(BusinessKpiValidation.TryParseRankingMetric(value, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("metric", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyMetricIsTreatedAsAbsentRatherThanInvalid()
    {
        Assert.True(BusinessKpiValidation.TryParseRankingMetric(
            string.Empty, out var metric, out _));
        Assert.Equal(BusinessRankingMetric.ApiRequests, metric);
    }

    // ── Limit ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultsTheLimitToTwenty()
    {
        Assert.True(BusinessKpiValidation.TryParseLimit(null, Options(), out var limit, out _));
        Assert.Equal(20, limit);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(1000, 100)]
    public void ClampsALargeLimitToTheConfiguredMaximum(int requested, int expected)
    {
        Assert.True(BusinessKpiValidation.TryParseLimit(requested, Options(), out var limit, out _));
        Assert.Equal(expected, limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void RejectsANonPositiveOrAbsurdLimit(int requested)
    {
        Assert.False(BusinessKpiValidation.TryParseLimit(requested, Options(), out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RespectsAConfiguredMaxRankingLimit()
    {
        Assert.True(BusinessKpiValidation.TryParseLimit(
            50, Options(o => o.MaxRankingLimit = 25), out var limit, out _));
        Assert.Equal(25, limit);
    }

    // ── Bucketing helper ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TruncatesDayWeekAndMonthOnTheDocumentedBoundaries()
    {
        var instant = new DateTime(2026, 9, 20, 13, 45, 0, DateTimeKind.Utc); // a Sunday

        Assert.Equal(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            BusinessKpiValidation.Truncate(instant, "day"));
        // ISO Monday of the week containing Sunday 20 Sep 2026 is Monday 14 Sep.
        Assert.Equal(new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc),
            BusinessKpiValidation.Truncate(instant, "week"));
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessKpiValidation.Truncate(instant, "month"));
    }

    [Fact]
    public void WeekTruncationIsStableOnAMonday()
    {
        var monday = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(monday, BusinessKpiValidation.Truncate(monday, "week"));
    }

    [Fact]
    public void MonthTruncationCrossesAYearBoundary()
    {
        var january = new DateTime(2027, 1, 15, 6, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessKpiValidation.Truncate(january, "month"));
    }
}
