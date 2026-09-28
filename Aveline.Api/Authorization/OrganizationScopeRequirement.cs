using Microsoft.AspNetCore.Authorization;

namespace Aveline.Api.Authorization;

/// <summary>
/// Demands that the caller holds an active, canonical organization membership for
/// the organization targeted by the request (route value <c>organizationId</c>) and,
/// when <see cref="Permission"/> is set, that the membership's boutique role grants it.
/// Membership is checked against the database, never the Clerk org claims alone.
/// </summary>
/// <remarks>
/// <see cref="Permission"/> is nullable so a route can ask for the thing it actually means —
/// "an active member of this organisation" — instead of borrowing <c>catalog:view</c> as a
/// generic member gate. That borrowing is how <c>GET …/usage</c> came to be gated by a catalog
/// read, and it is the reason <c>catalog:manage</c> could not be enforced without also taking
/// the camera and the label printer away from staff.
/// </remarks>
public sealed class OrganizationScopeRequirement : IAuthorizationRequirement
{
    public OrganizationScopeRequirement(string? permission = null)
    {
        Permission = permission;
    }

    /// <summary>
    /// Boutique permission required by the targeted resource, or <see langword="null"/> when
    /// an active membership alone satisfies the requirement.
    /// </summary>
    public string? Permission { get; }
}
