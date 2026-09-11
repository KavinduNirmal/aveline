namespace Aveline.Api.Modules.Billing.Models;

/// <summary>Per-organisation entitlement exception, used for Enterprise contracts.</summary>
public sealed class PlanEntitlementOverride
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public string Key { get; set; } = string.Empty;

    public EntitlementValueType ValueType { get; set; }

    public decimal? ValueDecimal { get; set; }

    public bool? ValueBool { get; set; }

    public string? ValueText { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
