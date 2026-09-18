using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A boutique customer. Tenant-scoped. Identified by phone number (unique per org).
/// <see cref="Status"/> tracks the customer lifecycle: new, returning, vip, dormant, deleted.
/// </summary>
public class Customer : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The boutique organization this customer belongs to (tenant scope).</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Primary contact / identity key. Unique per organization.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? FullName { get; set; }

    /// <summary>new | returning | vip | dormant | deleted</summary>
    public string Status { get; set; } = "new";

    public decimal TotalSpent { get; set; }

    public int VisitCount { get; set; }

    public DateTime? LastVisitAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }

    public Organization? Organization { get; set; }

    public User? CreatedByUser { get; set; }

    public ICollection<CustomerPreference> Preferences { get; set; } = [];

    public ICollection<CustomerEvent> Events { get; set; } = [];

    public ICollection<CustomerMemory> Memories { get; set; } = [];

    public ICollection<CustomerInteraction> Interactions { get; set; } = [];

    public ICollection<CustomerConsent> Consents { get; set; } = [];

    public ICollection<CustomerTag> Tags { get; set; } = [];
}
