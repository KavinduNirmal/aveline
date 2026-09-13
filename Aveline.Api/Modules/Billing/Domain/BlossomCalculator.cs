namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>
/// Pure Blossom arithmetic with no dependencies. Rounding and clamping are the
/// highest-risk arithmetic in the system and must be unit-testable without a database
/// or a configuration provider (implementation-plan.md §2.1).
/// </summary>
/// <remarks>
/// <c>BlossomUnits = max(round(NormalizedUnits / UnitsPerBlossom, RoundingDecimals),
/// MinimumChargeBlossoms)</c>, stored at 4 dp (BR-1.11). Rounding is applied once per
/// workflow, never per LLM call (BR-1.10).
/// </remarks>
public static class BlossomCalculator
{
    public const int MaxRoundingDecimals = 6;
    public const int StorageDecimals = 4;

    public static decimal Calculate(
        long normalizedUnits,
        int unitsPerBlossom,
        BlossomRoundingMode roundingMode,
        int roundingDecimals,
        decimal minimumChargeBlossoms)
    {
        if (unitsPerBlossom <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitsPerBlossom), unitsPerBlossom, "UnitsPerBlossom must be greater than zero.");
        }

        if (roundingDecimals is < 0 or > MaxRoundingDecimals)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roundingDecimals), roundingDecimals,
                $"RoundingDecimals must be between 0 and {MaxRoundingDecimals}.");
        }

        if (minimumChargeBlossoms < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumChargeBlossoms), minimumChargeBlossoms, "MinimumChargeBlossoms must not be negative.");
        }

        if (roundingMode == BlossomRoundingMode.HalfUp && roundingDecimals < 1)
        {
            throw new ArgumentException(
                "HalfUp rounding requires at least one decimal place.", nameof(roundingDecimals));
        }

        var raw = normalizedUnits / (decimal)unitsPerBlossom;
        var factor = PowerOfTen(roundingDecimals);

        var rounded = roundingMode switch
        {
            BlossomRoundingMode.Ceiling => Math.Ceiling(raw * factor) / factor,
            BlossomRoundingMode.Down => Math.Truncate(raw * factor) / factor,
            BlossomRoundingMode.Up => (raw >= 0 ? Math.Ceiling(raw * factor) : Math.Floor(raw * factor)) / factor,
            BlossomRoundingMode.HalfUp => Math.Round(raw, roundingDecimals, MidpointRounding.AwayFromZero),
            _ => throw new ArgumentOutOfRangeException(nameof(roundingMode), roundingMode, "Unknown rounding mode."),
        };

        var charge = Math.Max(rounded, minimumChargeBlossoms);
        return Math.Round(charge, StorageDecimals, MidpointRounding.AwayFromZero);
    }

    private static decimal PowerOfTen(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
