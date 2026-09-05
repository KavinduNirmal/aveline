using System.Security.Claims;
using Aveline.Api.Infrastructure.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Authenticated endpoint exposing the current user's Clerk role claims plus the
/// authoritative account lifecycle/context resolved by the middleware read model.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/claims", (ClaimsPrincipal user, HttpContext httpContext) =>
        {
            var rawClaims = user.Claims
                .Where(c => !c.Type.StartsWith("http://schemas.microsoft.com")
                         && !c.Type.StartsWith("http://schemas.xmlsoap.org"))
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray());

            // Set by OnboardingMiddleware from the synchronized read model (DB + caches).
            var currentUser = httpContext.Items["CurrentUser"] as UserOnboardingCacheItem;

            return Results.Ok(new
            {
                UserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
                Email = user.FindFirstValue("email"),
                Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
                Claims = rawClaims,
                Account = new
                {
                    HasCompletedOnboarding = currentUser?.HasCompletedOnboarding ?? false,
                    AccountState = currentUser?.AccountState.ToString(),
                    UserRole = currentUser?.UserRole,
                    OrganizationRole = currentUser?.OrganizationRole,
                },
            });
        }).RequireAuthorization();

        return endpoints;
    }
}
