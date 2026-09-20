using System.Security.Claims;
using Aveline.Api.Modules.ApiAccess.Authentication;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Resolves the telemetry/quota attribution from the authenticated principal. Resolution is
/// claim-only and never touches the database, so it stays off the request's critical path;
/// the API-key scheme supplies key and org directly, while a JWT may carry <c>org_id</c>
/// and <c>user_id</c>.
/// </summary>
/// <remarks>
/// A Guid claim always wins. A Clerk-shaped claim (<c>user_…</c>/<c>org_…</c>) is resolved
/// through <see cref="IClaimIdentityMap"/>, a synchronous in-memory dictionary built off the
/// request path by <see cref="ClaimIdentityMapRefresher"/>. Anything unresolvable is left
/// <c>null</c> (BR-6.1); this method never throws and never performs I/O.
/// </remarks>
public static class RequestPrincipal
{
    public const string OrganizationClaim = "org_id";
    public const string UserClaim = "user_id";

    /// <summary>
    /// Claim-only overload for callers that have no identity map (quota enforcement and any
    /// future non-telemetry consumer). Clerk-shaped claims resolve to <c>null</c> here.
    /// </summary>
    public static (Guid? OrganizationId, Guid? ApiKeyId, Guid? UserId) Resolve(
        ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return (null, null, null);
        }

        var organizationId =
            ParseGuid(principal.FindFirst(ApiKeyClaimTypes.OrganizationId)?.Value)
            ?? ParseGuid(principal.FindFirst(OrganizationClaim)?.Value);

        var apiKeyId = ParseGuid(principal.FindFirst(ApiKeyClaimTypes.ApiKeyId)?.Value);

        var userId =
            ParseGuid(principal.FindFirst(UserClaim)?.Value)
            ?? ParseGuid(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            ?? ParseGuid(principal.FindFirst("sub")?.Value);

        return (organizationId, apiKeyId, userId);
    }

    /// <summary>
    /// The attribution used by <see cref="ApiTelemetryMiddleware"/>: the same claim chain, with
    /// Clerk-shaped ids falling back to the in-memory claim map. One synchronous dictionary
    /// read, no I/O, no allocation beyond the tuple.
    /// </summary>
    public static (Guid? OrganizationId, Guid? ApiKeyId, Guid? UserId) Resolve(
        ClaimsPrincipal principal,
        IClaimIdentityMap identities)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return (null, null, null);
        }

        // API keys keep first refusal — their claims are already GUIDs.
        var organizationId =
            ParseGuid(principal.FindFirst(ApiKeyClaimTypes.OrganizationId)?.Value)
            ?? ResolveOrganization(principal, identities);

        var apiKeyId = ParseGuid(principal.FindFirst(ApiKeyClaimTypes.ApiKeyId)?.Value);

        var userId =
            ParseGuid(principal.FindFirst(UserClaim)?.Value)
            ?? ParseGuid(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            ?? ParseGuid(principal.FindFirst("sub")?.Value)
            ?? ResolveUser(principal, identities);

        return (organizationId, apiKeyId, userId);
    }

    private static Guid? ResolveOrganization(ClaimsPrincipal principal, IClaimIdentityMap identities)
    {
        var claim = principal.FindFirst(OrganizationClaim)?.Value;
        return ParseGuid(claim) ?? identities.ResolveOrganizationId(claim);
    }

    private static Guid? ResolveUser(ClaimsPrincipal principal, IClaimIdentityMap identities)
    {
        var claim = principal.FindFirst(UserClaim)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        return ParseGuid(claim) ?? identities.ResolveUserId(claim);
    }

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;
}
