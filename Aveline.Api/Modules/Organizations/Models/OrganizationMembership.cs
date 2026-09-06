using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Organizations.Models;

/// <summary>
/// A user's membership in an <see cref="Organization"/> carrying the canonical
/// boutique role (one of the <c>org:boutique_*</c> roles in the authorization
/// catalog). A user can be a member of many organizations; memberships become
/// <see cref="MembershipStatus.Active"/> only after an invitation is accepted.
/// </summary>
public class OrganizationMembership
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Canonical boutique role value, e.g. <c>org:boutique_staff</c>,
    /// <c>org:boutique_manager</c>, <c>org:boutique_owner</c>.
    /// </summary>
    public string BoutiqueRole { get; set; } = string.Empty;

    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }

    public Organization? Organization { get; set; }
}
