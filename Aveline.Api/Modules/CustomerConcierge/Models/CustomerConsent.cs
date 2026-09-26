using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A customer's data-processing consent for the boutique. Tenant-scoped, one row per customer.
/// When <see cref="ConsentStatus"/> is <c>revoked</c>, the agent must not process the customer's
/// messages. How that state was reached is recorded in the append-only
/// <see cref="ConsentAuditEntry"/> table; the two timestamps here are the effective-state summary.
/// </summary>
public class CustomerConsent : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>pending | granted | revoked (see <see cref="ConsentStatuses"/>).</summary>
    public string ConsentStatus { get; set; } = ConsentStatuses.Pending;

    public DateTime? ConsentGrantedAt { get; set; }

    public DateTime? ConsentRevokedAt { get; set; }

    /// <summary>
    /// How the current status was reached: <c>otp_link</c> | <c>staff</c> | <c>api</c> |
    /// <c>system</c>.
    /// </summary>
    public string? ConsentSource { get; set; }

    /// <summary>
    /// The customer's identity across organisations, so a global opt-out can be expressed by one
    /// subject rather than one row per boutique.
    /// </summary>
    public Guid? GlobalSubjectId { get; set; }

    /// <summary>When the customer was last shown the disclosure (welcome message).</summary>
    public DateTime? DisclosureShownAt { get; set; }

    /// <summary>Which disclosure version was shown, for policy versioning.</summary>
    public string? DisclosureVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }
}
