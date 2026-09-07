using System.Security.Claims;

namespace Aveline.Api.Authorization;

/// <summary>
/// Promotes the Clerk jwt-aveline-v1 role claims (<c>user_role</c>, <c>org_role</c>)
/// into standard <see cref="ClaimTypes.Role"/> claims so <c>[Authorize(Roles = "...")]</c>
/// and <c>IsInRole</c> evaluate both.
/// </summary>
public static class RoleClaimNormalizer
{
    private static readonly string[] RoleClaimTypes = { "user_role", "org_role" };

    public static void PromoteRoleClaims(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        foreach (var roleClaimType in RoleClaimTypes)
        {
            var value = principal.FindFirst(roleClaimType)?.Value;
            if (!string.IsNullOrEmpty(value))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, value.ToLowerInvariant()));
            }
        }
    }
}
