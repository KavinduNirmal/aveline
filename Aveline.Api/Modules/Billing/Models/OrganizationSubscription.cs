namespace Aveline.Api.Modules.Billing.Models;

/// <summary>Subscription billing cadence.</summary>
public enum BillingCycle
{
    Monthly,
    Annual,
}

/// <summary>Lifecycle of an organisation subscription.</summary>
public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Cancelled,
    Expired,
}

/// <summary>
/// One current subscription row per organisation (domain-model.md §4.4). Phase 3 attaches
/// a payment provider; the provider columns exist but no provider client is written.
/// </summary>
public sealed class OrganizationSubscription
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public PlanTier PlanTier { get; set; } = PlanTier.Seed;

    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTime CurrentPeriodStart { get; set; }

    public DateTime CurrentPeriodEnd { get; set; }

    public int SeatsIncluded { get; set; }

    public decimal PriceLkr { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    public string? ExternalProvider { get; set; }

    public string? ExternalSubscriptionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CancelledAt { get; set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL's <c>xmin</c>.</summary>
    public uint ConcurrencyToken { get; set; }
}
