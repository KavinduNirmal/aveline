using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Analytics.Models;

/// <summary>
/// One row per organization per UTC day: the tier and billing state as of the end of that day.
/// </summary>
/// <remarks>
/// This exists because <c>OrganizationSubscription</c> is one current row per organization
/// (unique on <c>OrganizationId</c>) created only when a plan changes or is cancelled — so it
/// can answer "what does this org pay" for billing orgs, but never "how many were on Bloom in
/// March", and it cannot answer the tier of an organization that never changed plan at all
/// (defect D-8). <see cref="PlanTier"/> therefore comes from <c>Organizations.PlanTier</c>,
/// which is authoritative for every organization.
/// </remarks>
public sealed class OrganizationSubscriptionSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>UTC date, start-of-day.</summary>
    public DateTime SnapshotDay { get; set; }

    /// <summary>From <c>Organizations.PlanTier</c> — present for every organization.</summary>
    public PlanTier PlanTier { get; set; }

    /// <summary>The billing row's status, or <see cref="SubscriptionStatus.Active"/> when there is none.</summary>
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    /// <summary>False when the organization has no <c>OrganizationSubscriptions</c> row (D-8).</summary>
    public bool HasBillingRow { get; set; }

    public int SeatsIncluded { get; set; }

    public decimal PriceLkr { get; set; }

    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    /// <summary>
    /// True when this row came from the audit-ledger reconstruction rather than a real
    /// snapshot. The console marks those buckets approximate.
    /// </summary>
    public bool IsBackfilled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
