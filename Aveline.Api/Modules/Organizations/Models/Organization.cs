namespace Aveline.Api.Modules.Organizations.Models;

/// <summary>
/// A boutique organization owned by a user (the boutique owner).
/// </summary>
/// <remarks>
/// This is the canonical membership record for ownership and staff membership
/// (see ADR-009 / the authorization catalog). The denormalized
/// <c>User.OrganizationId</c>/<c>OrganizationRole</c> fields are legacy and are
/// migrated to memberships (see #50 notes).
/// </remarks>
public class Organization
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    /// <summary>Stable, unique human-readable identifier used in links and routing.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// The Clerk organization id (<c>org_...</c>), the external key carried by the
    /// JWT <c>org_id</c> claim and the legacy <c>User.OrganizationId</c> value.
    /// Unique so Clerk org memberships/webhooks can be matched to this row.
    /// </summary>
    public string? ClerkOrgId { get; set; }

    /// <summary>Owner identity (the user who created the organization).</summary>
    public Guid OwnerUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrganizationMembership> Memberships { get; set; } = [];

    public ICollection<OrganizationInvitation> Invitations { get; set; } = [];
}
