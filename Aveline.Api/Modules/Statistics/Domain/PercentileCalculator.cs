namespace Aveline.Api.Modules.Statistics.Domain;

/// <summary>
/// Linear-interpolation percentiles, implemented so that the on-the-fly query path
/// produces the same numbers as PostgreSQL
/// <c>percentile_cont(p) WITHIN GROUP (ORDER BY value)</c> (statistics-catalog.md S-16).
/// </summary>
/// <remarks>
/// PostgreSQL defines <c>percentile_cont</c> over the ordered sample as
/// <c>value[floor(rank)] + (rank - floor(rank)) * (value[ceil(rank)] - value[floor(rank)])</c>
/// where <c>rank = p * (n - 1)</c>. The sample is never assumed to be sorted.
/// </remarks>
public static class PercentileCalculator
{
    /// <summary>
    /// Returns the interpolated <paramref name="percentile"/> (0.0–1.0) of the sample, or
    /// <c>null</c> when the sample is empty. Values outside 0–1 are clamped.
    /// </summary>
    public static double? Percentile(IEnumerable<double> values, double percentile)
    {
        var sample = values as IReadOnlyList<double> ?? values.ToArray();
        if (sample.Count == 0)
        {
            return null;
        }

        return PercentileCore(sample, percentile);
    }

    /// <summary>
    /// Returns the interpolated percentile of a nullable sample, ignoring <c>null</c>
    /// entries (an absent duration is not a zero duration). <c>null</c> when nothing remains.
    /// </summary>
    public static double? Percentile(IEnumerable<double?> values, double percentile)
    {
        var sample = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return sample.Length == 0 ? null : PercentileCore(sample, percentile);
    }

    /// <summary>Convenience overload for integer samples such as <c>DurationMs</c>.</summary>
    public static double? Percentile(IEnumerable<int> values, double percentile) =>
        Percentile(values.Select(value => (double)value), percentile);

    /// <summary>Convenience overload for nullable integer samples such as <c>DurationMs</c>.</summary>
    public static double? Percentile(IEnumerable<int?> values, double percentile) =>
        Percentile(values.Select(value => (double?)value), percentile);

    private static double PercentileCore(IReadOnlyList<double> sample, double percentile)
    {
        var p = Math.Clamp(percentile, 0d, 1d);

        var sorted = sample.ToArray();
        Array.Sort(sorted);

        var rank = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);

        if (lower == upper)
        {
            return sorted[lower];
        }

        var fraction = rank - lower;
        return sorted[lower] + (fraction * (sorted[upper] - sorted[lower]));
    }
}
