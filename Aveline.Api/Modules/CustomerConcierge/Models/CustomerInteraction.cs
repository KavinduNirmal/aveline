using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A single interaction with a customer (WhatsApp, Instagram, in-person, phone). Tenant-scoped.
/// <see cref="ParsedIntentJson"/> stores the structured intent extracted from the message (JSONB).
/// </summary>
public class CustomerInteraction : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>whatsapp | instagram | in_person | phone</summary>
    public string Channel { get; set; } = "whatsapp";

    /// <summary>inbound | outbound</summary>
    public string Direction { get; set; } = "inbound";

    public string? MessageContent { get; set; }

    /// <summary>Structured intent extracted from the message, serialized as JSON (jsonb).</summary>
    public string ParsedIntentJson { get; set; } = "{}";

    public Guid? StaffMemberId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }

    public User? StaffMember { get; set; }
}
