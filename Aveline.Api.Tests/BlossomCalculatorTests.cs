using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #183 — pure Blossom arithmetic (BR-1.1, BR-1.10, BR-1.11). This is the
/// highest-risk arithmetic in the system, so it is tested without a database.
/// </summary>
public class BlossomCalculatorTests
{
    private const int UnitsPerBlossom = 1000;
    private const decimal Minimum = 0.1m;

    [Theory]
    [InlineData(0L)]
    [InlineData(100L)]
    [InlineData(999L)]
    [InlineData(1000L)]
    [InlineData(1700L)]
    [InlineData(12345L)]
    [InlineData(1_000_000L)]
    public void Calculate_WithDefaults_MatchesLegacyFormula(long normalizedUnits)
    {
        var expected = UsageTrackerService.CalculateBlossomUnits(
            (int)Math.Min(normalizedUnits, int.MaxValue), 0, 0);

        var actual = BlossomCalculator.Calculate(
            normalizedUnits, UnitsPerBlossom, BlossomRoundingMode.Ceiling, 1, Minimum);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Calculate_ZeroTokens_ReturnsMinimumCharge()
    {
        var actual = BlossomCalculator.Calculate(
            0, UnitsPerBlossom, BlossomRoundingMode.Ceiling, 1, Minimum);

        Assert.Equal(Minimum, actual);
    }

    [Theory]
    [InlineData(BlossomRoundingMode.Ceiling, 1.24)]
    [InlineData(BlossomRoundingMode.HalfUp, 1.23)]
    [InlineData(BlossomRoundingMode.Down, 1.23)]
    [InlineData(BlossomRoundingMode.Up, 1.24)]
    public void Calculate_AppliesRoundingMode(BlossomRoundingMode mode, decimal expected)
    {
        // 1234 / 1000 = 1.234, rounded to 2 dp.
        var actual = BlossomCalculator.Calculate(1234, UnitsPerBlossom, mode, 2, Minimum);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Calculate_HalfUp_RoundsMidpointAwayFromZero()
    {
        // 1235 / 1000 = 1.235 -> 1.24 at 2 dp under half-up.
        var actual = BlossomCalculator.Calculate(1235, UnitsPerBlossom, BlossomRoundingMode.HalfUp, 2, Minimum);

        Assert.Equal(1.24m, actual);
    }

    [Fact]
    public void Calculate_SmallRemainder_RespectsRoundingMode()
    {
        // 1001 / 1000 = 1.001.
        Assert.Equal(1.1m, BlossomCalculator.Calculate(1001, UnitsPerBlossom, BlossomRoundingMode.Ceiling, 1, Minimum));
        Assert.Equal(1.0m, BlossomCalculator.Calculate(1001, UnitsPerBlossom, BlossomRoundingMode.Down, 1, Minimum));
        Assert.Equal(1.1m, BlossomCalculator.Calculate(1001, UnitsPerBlossom, BlossomRoundingMode.Up, 1, Minimum));
        Assert.Equal(1.0m, BlossomCalculator.Calculate(1001, UnitsPerBlossom, BlossomRoundingMode.HalfUp, 1, Minimum));
    }

    [Fact]
    public void Calculate_ClampsToMinimumCharge()
    {
        var actual = BlossomCalculator.Calculate(
            50, UnitsPerBlossom, BlossomRoundingMode.Down, 1, 0.5m);

        Assert.Equal(0.5m, actual);
    }

    [Fact]
    public void Calculate_StoresAtFourDecimalPlaces()
    {
        // 2 / 3 = 0.666667 (ceiling at 6 dp), stored rounded to 4 dp.
        var actual = BlossomCalculator.Calculate(
            2, 3, BlossomRoundingMode.Ceiling, 6, 0m);

        Assert.Equal(0.6667m, actual);
    }

    [Fact]
    public void Calculate_LargeValues_DoNotOverflow()
    {
        var actual = BlossomCalculator.Calculate(
            10_000_000_000, UnitsPerBlossom, BlossomRoundingMode.Ceiling, 1, Minimum);

        Assert.Equal(10_000_000m, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_NonPositiveUnitsPerBlossom_Throws(int unitsPerBlossom)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlossomCalculator.Calculate(
            1000, unitsPerBlossom, BlossomRoundingMode.Ceiling, 1, Minimum));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Calculate_RoundingDecimalsOutOfRange_Throws(int decimals)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlossomCalculator.Calculate(
            1000, UnitsPerBlossom, BlossomRoundingMode.Ceiling, decimals, Minimum));
    }

    [Fact]
    public void Calculate_NegativeMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlossomCalculator.Calculate(
            1000, UnitsPerBlossom, BlossomRoundingMode.Ceiling, 1, -0.1m));
    }

    [Fact]
    public void Calculate_HalfUpWithZeroDecimals_Throws()
    {
        Assert.Throws<ArgumentException>(() => BlossomCalculator.Calculate(
            1000, UnitsPerBlossom, BlossomRoundingMode.HalfUp, 0, Minimum));
    }
}
