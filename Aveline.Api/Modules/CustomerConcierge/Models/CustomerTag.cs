using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A free-form tag attached to a customer (e.g. "wedding-season", "prefers-consultation").
/// Tenant-scoped. Unique per customer.
/// </summary>
public class CustomerTag : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    public string Tag { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }
}
