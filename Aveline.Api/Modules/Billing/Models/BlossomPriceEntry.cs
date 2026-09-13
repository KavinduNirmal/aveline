namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// The effective-dated commercial price of Blossoms (domain-model.md §3.2). Used for
/// revenue reporting and top-up pricing, never to compute consumption.
/// </summary>
public sealed class BlossomPriceEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary><c>null</c> applies the price to every plan tier.</summary>
    public PlanTier? PlanTier { get; set; }

    /// <summary><c>null</c> is a tier price; set is a per-organization contract override.</summary>
    public Guid? OrganizationId { get; set; }

    public BlossomSkuKind SkuKind { get; set; }

    public string? SkuCode { get; set; }

    public decimal BlossomQuantity { get; set; }

    public decimal PriceLkr { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public BlossomRuleStatus Status { get; set; } = BlossomRuleStatus.Draft;

    public string ChangeReason { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
