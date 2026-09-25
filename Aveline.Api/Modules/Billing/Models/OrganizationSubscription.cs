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

    /// <summary>
    /// How many renewal attempts the dunning window has made, including the rollover's own attempt
    /// at the period boundary. <c>0</c> while the subscription is current.
    /// </summary>
    /// <remarks>
    /// Plan §9.4 F4 / decision Q3: a failed renewal retries on days 1, 3 and 7, so this reaches
    /// <c>4</c> (the boundary attempt plus three retries) before the schedule is exhausted, and the
    /// subscription expires on day 14. It is persisted rather than derived because a job that runs
    /// late or twice must not lose or double its place in the schedule.
    /// </remarks>
    public int RenewalAttemptCount { get; set; }

    /// <summary>When the next dunning retry is due, or <c>null</c> when none remains.</summary>
    public DateTime? NextRenewalAttemptAt { get; set; }

    /// <summary>
    /// The period boundary the dunning window is anchored to, set when the first renewal attempt
    /// fails. Day 14 from here is <see cref="SubscriptionStatus.Expired"/>.
    /// </summary>
    public DateTime? DunningStartedAt { get; set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL's <c>xmin</c>.</summary>
    public uint ConcurrencyToken { get; set; }
}
