using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Authenticated endpoints for the current user's Clerk profile and role claims.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/claims", (ClaimsPrincipal user) =>
        {
            var rawClaims = user.Claims
                .Where(c => !c.Type.StartsWith("http://schemas.microsoft.com")
                         && !c.Type.StartsWith("http://schemas.xmlsoap.org"))
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray());

            return Results.Ok(new
            {
                UserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
                Email = user.FindFirstValue("email"),
                Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
                Claims = rawClaims,
            });
        }).RequireAuthorization();

        return endpoints;
    }
}
