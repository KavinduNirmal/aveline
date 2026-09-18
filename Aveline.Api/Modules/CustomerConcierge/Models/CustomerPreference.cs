using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A single customer preference (e.g. "prefers silk", "dislikes flashy"). Tenant-scoped.
/// <see cref="IsExplicit"/> distinguishes a stated fact from an inferred one; <see cref="Confidence"/>
/// records how strongly the preference is believed.
/// </summary>
public class CustomerPreference : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>Canonical preference key, e.g. "fabric", "color", "style", "budget".</summary>
    public string PreferenceKey { get; set; } = string.Empty;

    /// <summary>The preference value, e.g. "silk", "pastel", "minimal".</summary>
    public string PreferenceValue { get; set; } = string.Empty;

    /// <summary>True when the customer stated it directly; false when inferred from behaviour.</summary>
    public bool IsExplicit { get; set; }

    /// <summary>0.00 - 1.00 confidence in the preference.</summary>
    public decimal Confidence { get; set; } = 0.50m;

    /// <summary>conversation | staff_note | purchase | inferred</summary>
    public string Source { get; set; } = "conversation";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }
}
