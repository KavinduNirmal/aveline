namespace Aveline.Api.Modules.Statistics.Domain;

/// <summary>Percentiles in milliseconds, or <c>null</c> with a reason when unavailable.</summary>
public sealed record LatencyPercentiles(double? P50Ms, double? P95Ms, double? P99Ms, string? Reason);

/// <summary>
/// The fixed cumulative (<c>le</c>) latency histogram behind S-26. Percentiles are obtained
/// by linear interpolation within the bucket that contains the target rank — approximate by
/// construction, which is why responses report a bucket-interpolated precision.
/// </summary>
public static class LatencyBuckets
{
    /// <summary>Upper bounds in milliseconds for buckets 0–10; bucket 11 is overflow.</summary>
    public static readonly int[] UpperBoundsMs =
        [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000];

    /// <summary>11 bounded buckets plus the overflow bucket (domain-model.md §7.1).</summary>
    public const int Count = 12;

    /// <summary>Reason returned when the sample floor is not met (BR-6.8).</summary>
    public const string InsufficientSamplesReason = "insufficient_samples";

    /// <summary>The index of the smallest bucket whose upper bound is &gt;= the duration.</summary>
    public static int Assign(int durationMs)
    {
        for (var i = 0; i < UpperBoundsMs.Length; i++)
        {
            if (durationMs <= UpperBoundsMs[i])
            {
                return i;
            }
        }

        return UpperBoundsMs.Length; // overflow
    }

    /// <summary>Adds one observation to a cumulative histogram in place.</summary>
    public static void Add(int[] cumulativeCounts, int durationMs)
    {
        for (var i = Assign(durationMs); i < cumulativeCounts.Length; i++)
        {
            cumulativeCounts[i]++;
        }
    }

    public static LatencyPercentiles Compute(
        IReadOnlyList<int> cumulativeCounts, long totalCount, int minSamples)
    {
        if (totalCount < Math.Max(1, minSamples))
        {
            return new LatencyPercentiles(null, null, null, InsufficientSamplesReason);
        }

        return new LatencyPercentiles(
            Interpolate(cumulativeCounts, totalCount, 0.50),
            Interpolate(cumulativeCounts, totalCount, 0.95),
            Interpolate(cumulativeCounts, totalCount, 0.99),
            null);
    }

    private static double? Interpolate(IReadOnlyList<int> cumulativeCounts, long total, double quantile)
    {
        if (total <= 0 || cumulativeCounts.Count == 0)
        {
            return null;
        }

        var rank = quantile * total;
        var cumulativeBelow = 0;

        for (var i = 0; i < cumulativeCounts.Count; i++)
        {
            var cumulative = cumulativeCounts[i];
            if (cumulative >= rank)
            {
                var lower = i == 0 ? 0d : UpperBoundsMs[i - 1];
                // The overflow bucket has no upper bound; report its lower edge conservatively.
                var upper = i < UpperBoundsMs.Length ? UpperBoundsMs[i] : lower;
                var bucketCount = cumulative - cumulativeBelow;
                if (bucketCount <= 0)
                {
                    return lower;
                }

                return lower + (upper - lower) * ((rank - cumulativeBelow) / bucketCount);
            }

            cumulativeBelow = cumulative;
        }

        return UpperBoundsMs[^1];
    }
}
