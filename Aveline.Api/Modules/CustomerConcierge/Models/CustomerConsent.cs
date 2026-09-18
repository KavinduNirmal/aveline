using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A customer's data-processing consent for the boutique. Tenant-scoped, one row per customer.
/// When <see cref="ConsentStatus"/> is <c>revoked</c>, the agent must not process the customer's
/// messages. <see cref="RevokeToken"/> allows a customer to revoke consent out-of-band.
/// </summary>
public class CustomerConsent : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>pending | granted | revoked</summary>
    public string ConsentStatus { get; set; } = "pending";

    public DateTime? ConsentGrantedAt { get; set; }

    public DateTime? ConsentRevokedAt { get; set; }

    public string? RevokeToken { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }
}
