using Aveline.Api.Modules.Statistics.Domain;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #222 — the 12 cumulative latency buckets and the percentile interpolation used by
/// S-26. Below the sample floor the percentile is <c>null</c> with a reason (BR-6.8).
/// </summary>
public class LatencyBucketTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(25, 2)]
    [InlineData(26, 3)]
    [InlineData(100, 4)]
    [InlineData(1000, 7)]
    [InlineData(1001, 8)]
    [InlineData(10000, 10)]
    [InlineData(10001, 11)]
    [InlineData(999999, 11)]
    public void AssignsTheSmallestBucketWhoseBoundCoversTheDuration(int durationMs, int expected)
    {
        Assert.Equal(expected, LatencyBuckets.Assign(durationMs));
    }

    [Fact]
    public void HasTwelveCumulativeBuckets()
    {
        Assert.Equal(12, LatencyBuckets.Count);
        Assert.Equal(11, LatencyBuckets.UpperBoundsMs.Length);
    }

    [Fact]
    public void CumulativeCountsAreNonDecreasingAndTheLastEqualsTheTotal()
    {
        int[] counts = new int[LatencyBuckets.Count];
        foreach (var duration in new[] { 1, 3, 7, 60, 700, 3000, 20000 })
        {
            LatencyBuckets.Add(counts, duration);
        }

        Assert.Equal(7, counts[^1]);
        for (var i = 1; i < counts.Length; i++)
        {
            Assert.True(counts[i] >= counts[i - 1]);
        }
    }

    [Fact]
    public void BelowTheFloor_ReturnsNullPercentilesWithAReason()
    {
        int[] counts = new int[LatencyBuckets.Count];
        for (var i = 0; i < 5; i++)
        {
            LatencyBuckets.Add(counts, 3);
        }

        var result = LatencyBuckets.Compute(counts, totalCount: 5, minSamples: 20);

        Assert.Null(result.P50Ms);
        Assert.Null(result.P95Ms);
        Assert.Null(result.P99Ms);
        Assert.Equal(LatencyBuckets.InsufficientSamplesReason, result.Reason);
    }

    [Fact]
    public void AtTheFloor_InterpolatesWithinTheContainingBucket()
    {
        // 20 samples all at <=5 ms: p50 = 0 + (5-0) * (10/20) = 2.5
        int[] counts = new int[LatencyBuckets.Count];
        for (var i = 0; i < 20; i++)
        {
            LatencyBuckets.Add(counts, 5);
        }

        var result = LatencyBuckets.Compute(counts, totalCount: 20, minSamples: 20);

        Assert.Equal(2.5, result.P50Ms);
        Assert.Equal(4.75, result.P95Ms);
        Assert.Equal(4.95, result.P99Ms);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void InterpolatesAcrossBuckets_WhereTheQuantileLands()
    {
        // 10 samples in the 0-5 bucket then 10 in the 100-250 bucket.
        int[] counts = new int[LatencyBuckets.Count];
        for (var i = 0; i < 10; i++)
        {
            LatencyBuckets.Add(counts, 3);
        }

        for (var i = 0; i < 10; i++)
        {
            LatencyBuckets.Add(counts, 150);
        }

        var result = LatencyBuckets.Compute(counts, totalCount: 20, minSamples: 20);
        Assert.NotNull(result.P50Ms);
        Assert.NotNull(result.P95Ms);
        Assert.NotNull(result.P99Ms);
        Assert.True(result.P95Ms > result.P50Ms);
    }
}
