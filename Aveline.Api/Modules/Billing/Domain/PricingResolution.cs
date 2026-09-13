using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>The immutable pricing facts applied to one workflow.</summary>
public sealed record PricingRuleSnapshot(
    Guid? RuleId,
    int Version,
    int UnitsPerBlossom,
    decimal MinimumChargeBlossoms,
    BlossomRoundingMode RoundingMode,
    int RoundingDecimals)
{
    /// <summary>
    /// The hard-coded fallback used when no rule matches (BR-1.9). It reproduces the
    /// legacy formula exactly: 1000 units per Blossom, ceiling, 1 decimal, minimum 0.1.
    /// </summary>
    public static PricingRuleSnapshot Fallback { get; } =
        new(null, 0, 1000, 0.1m, BlossomRoundingMode.Ceiling, 1);
}

/// <summary>The resolution result, including whether the fallback was used.</summary>
public sealed record PricingResolution(PricingRuleSnapshot Rule, bool IsFallback);
