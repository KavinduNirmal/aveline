using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Payments.Models;

/// <summary>
/// The provider-neutral record of one charge attempt (plan §6.3, decision D1). The provider is a
/// dependency of the intent, not the other way round: the row stores only what Aveline needs to
/// know, so a provider with a fundamentally different model still fits.
/// </summary>
/// <remarks>
/// Deliberately carries no card, PAN, CVC, expiry, or token field (constraint C11). There is no
/// column a client could use to send card data to Aveline.
/// </remarks>
public sealed class PaymentIntent : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>The adapter key that created this intent: <c>"manual" | "mock" | "stripe"</c>.</summary>
    public string Provider { get; set; } = "manual";

    /// <summary>
    /// The provider's id for the charge. Unique per provider under a filtered index, so a replayed
    /// create cannot produce a second row for the same provider charge.
    /// </summary>
    public string? ProviderIntentId { get; set; }

    public PaymentPurpose Purpose { get; set; } = PaymentPurpose.BlossomTopUp;

    public PaymentProviderStatus Status { get; set; } = PaymentProviderStatus.RequiresAction;

    /// <summary>The amount in integer minor units, because that is what providers take (D3).</summary>
    public long AmountMinor { get; set; }

    public string Currency { get; set; } = "LKR";

    /// <summary>
    /// The list price the intent was created from, kept so a later price change does not
    /// retro-explain the charge.
    /// </summary>
    public decimal PriceLkr { get; set; }

    /// <summary>Set for <see cref="PaymentPurpose.BlossomTopUp"/>.</summary>
    public string? SkuCode { get; set; }

    /// <summary>Set for <see cref="PaymentPurpose.BlossomTopUp"/>.</summary>
    public decimal? BlossomQuantity { get; set; }

    /// <summary>Set for subscription purposes.</summary>
    public PlanTier? PlanTier { get; set; }

    public DateTime? BillingPeriodStart { get; set; }

    public DateTime? BillingPeriodEnd { get; set; }

    public string? ProviderSubscriptionId { get; set; }

    /// <summary>
    /// Client-supplied. Unique per <c>(OrganizationId, Purpose)</c> under a filtered index, which is
    /// what makes a retried create resolve to the intent it already made.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? FailureCode { get; set; }

    public string? FailureMessage { get; set; }

    /// <summary>Null for a system-created intent such as a renewal.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The settlement identity guard; a <c>Succeeded</c> intent without one is rejected by
    /// <c>CK_PaymentIntents_Settled</c>.
    /// </summary>
    public DateTime? SettledAt { get; set; }

    public DateTime? RefundedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>The <c>SourceRef</c> Aveline wrote into the ledgers, kept for reconciliation.</summary>
    public string? ExternalRef { get; set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL's <c>xmin</c>.</summary>
    public uint ConcurrencyToken { get; set; }
}
