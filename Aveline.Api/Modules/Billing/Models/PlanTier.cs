using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// The subscription plan tiers available in Aveline.
/// Treated as string-backed enums across JSON APIs and persistence.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlanTier
{
    /// <summary>Free tier — 150 Blossoms, 1 staff, 50 active customers.</summary>
    Seed,

    /// <summary>LKR 3,500/month — 750 Blossoms, 3 staff, 250 active customers.</summary>
    Bloom,

    /// <summary>LKR 9,000/month — 2,000 Blossoms, 10 staff, 1,000 active customers.</summary>
    Orchid,

    /// <summary>LKR 20,000/month — 5,000 Blossoms, 25 staff, 5,000 active customers.</summary>
    Rose,

    /// <summary>Custom pricing — negotiated per requirements.</summary>
    Enterprise,
}
