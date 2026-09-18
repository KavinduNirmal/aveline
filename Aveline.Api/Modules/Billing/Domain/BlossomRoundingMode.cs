namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>Rounding applied to the token to Blossom conversion (BR-1.6).</summary>
public enum BlossomRoundingMode
{
    /// <summary>Round up to the configured number of decimals (legacy default).</summary>
    Ceiling,

    /// <summary>Round half away from zero; requires at least one decimal place.</summary>
    HalfUp,

    /// <summary>Truncate toward zero.</summary>
    Down,

    /// <summary>Round away from zero.</summary>
    Up,
}
