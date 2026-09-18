using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// An upcoming or past event relevant to a customer (wedding, birthday, party, office, other).
/// Tenant-scoped. Used to prepare staff for interactions and to trigger reminders.
/// </summary>
public class CustomerEvent : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>wedding | birthday | party | office | other</summary>
    public string EventType { get; set; } = "other";

    public DateTime EventDate { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? ReminderSentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }
}
