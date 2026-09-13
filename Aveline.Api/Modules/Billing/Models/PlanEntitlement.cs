namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// Effective-dated plan limits, replacing the three hardcoded copies (defect D-12).
/// </summary>
public sealed class PlanEntitlement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public PlanTier PlanTier { get; set; }

    public string Key { get; set; } = string.Empty;

    public EntitlementValueType ValueType { get; set; }

    /// <summary>Used for <see cref="EntitlementValueType.Integer"/> and <c>Decimal</c>.</summary>
    public decimal? ValueDecimal { get; set; }

    public bool? ValueBool { get; set; }

    public string? ValueText { get; set; }

    /// <summary>Allows disabling a capability without deleting the row.</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
