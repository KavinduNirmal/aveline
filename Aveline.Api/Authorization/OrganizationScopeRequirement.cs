using Microsoft.AspNetCore.Authorization;

namespace Aveline.Api.Authorization;

/// <summary>
/// Demands that the caller holds an active, canonical organization membership for
/// the organization targeted by the request (route value <c>organizationId</c>) and
/// that the membership's boutique role grants <see cref="Permission"/>. Membership is
/// checked against the database, never the Clerk org claims alone.
/// </summary>
public sealed class OrganizationScopeRequirement : IAuthorizationRequirement
{
    public OrganizationScopeRequirement(string permission)
    {
        Permission = permission;
    }

    /// <summary>Boutique permission required by the targeted resource.</summary>
    public string Permission { get; }
}
