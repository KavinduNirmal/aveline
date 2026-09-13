using Aveline.Api.Modules.ApiAccess.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace Aveline.Api.Modules.ApiAccess.Middleware;

/// <summary>
/// Returns 404 for an API key used against another organization's route, before the
/// authorization layer can answer 403 (plan §8.2). A 403 would confirm that the target
/// organization exists; a 404 makes an owned and a non-existent organization
/// indistinguishable.
/// </summary>
/// <remarks>
/// The API-key scheme is not the default scheme, so <c>UseAuthentication</c> does not run
/// it; this middleware authenticates it explicitly when the header is present and caches
/// the result for the authorization layer.
/// </remarks>
public sealed class ApiKeyTenantScopeMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyTenantScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName))
        {
            var result = await context.AuthenticateAsync(ApiKeyAuthenticationHandler.SchemeName);
            if (result.Succeeded && result.Principal is { } principal)
            {
                var routeValue = context.Request.RouteValues["organizationId"]?.ToString();
                var claimValue = principal.FindFirst(ApiKeyClaimTypes.OrganizationId)?.Value;

                if (Guid.TryParse(routeValue, out var routeOrganizationId)
                    && Guid.TryParse(claimValue, out var keyOrganizationId)
                    && routeOrganizationId != keyOrganizationId)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(new { message = "Organization not found." });
                    return;
                }
            }
        }

        await _next(context);
    }
}
