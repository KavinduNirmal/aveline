using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// Pre-computed daily summary rollup for AI consumption and Blossom usage by organization,
/// plan tier, provider, and model (S-4, S-5, S-6, statistics-catalog.md §9).
/// </summary>
public class DailyBillingMetric
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public DateTime Day { get; set; }

    public PlanTier PlanTier { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int RequestCount { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CachedTokens { get; set; }

    public decimal ActualCostUsd { get; set; }

    public decimal BlossomUnits { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
