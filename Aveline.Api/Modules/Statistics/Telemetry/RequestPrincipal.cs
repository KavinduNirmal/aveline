using System.Security.Claims;
using Aveline.Api.Modules.ApiAccess.Authentication;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Resolves the telemetry/quota attribution from the authenticated principal. Resolution is
/// claim-only and never touches the database, so it stays off the request's critical path;
/// the API-key scheme supplies key and org directly, while a JWT may carry <c>org_id</c>
/// and <c>user_id</c>. Anything unresolvable is left <c>null</c> (BR-6.1).
/// </summary>
public static class RequestPrincipal
{
    public const string OrganizationClaim = "org_id";
    public const string UserClaim = "user_id";

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

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;
}
