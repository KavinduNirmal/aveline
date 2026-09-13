using Aveline.Api.Modules.Statistics.Domain;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #210 — linear-interpolation percentiles that match PostgreSQL
/// <c>percentile_cont</c> so the on-the-fly path and a future rollup agree.
/// </summary>
public class PercentileCalculatorTests
{
    [Fact]
    public void EmptySample_ReturnsNull()
    {
        Assert.Null(PercentileCalculator.Percentile(Array.Empty<double>(), 0.5));
    }

    [Fact]
    public void SingleValue_ReturnsThatValueForEveryPercentile()
    {
        var sample = new[] { 10.0 };

        Assert.Equal(10.0, PercentileCalculator.Percentile(sample, 0.0));
        Assert.Equal(10.0, PercentileCalculator.Percentile(sample, 0.5));
        Assert.Equal(10.0, PercentileCalculator.Percentile(sample, 1.0));
    }

    [Fact]
    public void Median_InterpolatesBetweenNeighbours()
    {
        // rank = 0.5 * (4 - 1) = 1.5 -> halfway between 2 and 3.
        Assert.Equal(2.5, PercentileCalculator.Percentile(new[] { 1.0, 2.0, 3.0, 4.0 }, 0.5));
    }

    [Fact]
    public void P95_InterpolatesBetweenNeighbours()
    {
        // rank = 0.95 * (5 - 1) = 3.8 -> 4 + 0.8 * (5 - 4) = 4.8.
        Assert.Equal(4.8, PercentileCalculator.Percentile(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }, 0.95));
    }

    [Fact]
    public void Boundaries_ReturnMinimumAndMaximum()
    {
        var sample = new[] { 1.0, 2.0, 3.0, 4.0 };

        Assert.Equal(1.0, PercentileCalculator.Percentile(sample, 0.0));
        Assert.Equal(4.0, PercentileCalculator.Percentile(sample, 1.0));
    }

    [Fact]
    public void UnsortedSample_IsSortedBeforeInterpolation()
    {
        Assert.Equal(3.0, PercentileCalculator.Percentile(new[] { 5.0, 1.0, 3.0, 2.0, 4.0 }, 0.5));
    }

    [Fact]
    public void MatchesPostgresPercentileCont_ForAKnownDistribution()
    {
        // percentile_cont(0.25) WITHIN GROUP (ORDER BY v) over {1,2,3,4,5} = 2.
        Assert.Equal(2.0, PercentileCalculator.Percentile(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }, 0.25));

        // percentile_cont(0.75) = 4.
        Assert.Equal(4.0, PercentileCalculator.Percentile(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }, 0.75));
    }

    [Fact]
    public void PercentileOutsideZeroToOne_IsClamped()
    {
        var sample = new[] { 1.0, 2.0, 3.0 };

        Assert.Equal(1.0, PercentileCalculator.Percentile(sample, -0.5));
        Assert.Equal(3.0, PercentileCalculator.Percentile(sample, 1.5));
    }

    [Fact]
    public void NullableSample_IgnoresNulls()
    {
        Assert.Equal(2.5, PercentileCalculator.Percentile(
            new double?[] { 1.0, null, 4.0 }, 0.5));
        Assert.Null(PercentileCalculator.Percentile(new double?[] { null, null }, 0.5));
    }
}
