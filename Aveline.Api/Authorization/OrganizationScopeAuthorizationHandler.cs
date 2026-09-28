using System.Security.Claims;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Authorization;

/// <summary>
/// Evaluates <see cref="OrganizationScopeRequirement"/> against the canonical
/// membership tables. The caller is resolved from the Clerk <c>sub</c> claim, the
/// target organization from the <c>organizationId</c> route value, and the
/// membership must be <see cref="MembershipStatus.Active"/> with a boutique role
/// that grants the required permission. This makes tenant scope independent of the
/// still-valid (and possibly stale) org claims carried by the JWT.
/// </summary>
public sealed class OrganizationScopeAuthorizationHandler
    : AuthorizationHandler<OrganizationScopeRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrganizationScopeAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationScopeRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        var organizationIdValue = httpContext.Request.RouteValues["organizationId"]?.ToString();
        if (string.IsNullOrWhiteSpace(organizationIdValue)
            || !Guid.TryParse(organizationIdValue, out var organizationId))
        {
            return;
        }

        // API keys are already tenant-fixed by the scheme; the middleware returns 404 for a
        // mismatched route, so here we only need to evaluate the granted scope.
        if (context.User.HasClaim(claim => claim.Type == Modules.ApiAccess.Authentication.ApiKeyClaimTypes.ApiKeyId))
        {
            var scopes = context.User
                .FindAll(Modules.ApiAccess.Authentication.ApiKeyClaimTypes.Scope)
                .Select(c => c.Value);

            // A null permission is the permission-free member gate: the middleware has already
            // fixed the key to its tenant, so any granted scope satisfies it.
            if (requirement.Permission is null
                || scopes.Contains(requirement.Permission, StringComparer.Ordinal))
            {
                context.Succeed(requirement);
            }

            return;
        }

        var clerkId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return;
        }

        var userRepository = httpContext.RequestServices.GetService(typeof(IUserRepository)) as IUserRepository;
        var organizationRepository = httpContext.RequestServices.GetService(typeof(IOrganizationRepository)) as IOrganizationRepository;
        if (userRepository is null || organizationRepository is null)
        {
            return;
        }

        var user = await userRepository.GetByClerkIdAsync(clerkId);
        if (user is null)
        {
            return;
        }

        var membership = await organizationRepository.GetMembershipAsync(organizationId, user.Id);
        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            return;
        }

        // A null permission is the permission-free member gate (`BoutiqueMemberPolicy`):
        // an active membership in the target organisation is the whole requirement.
        if (requirement.Permission is null
            || Permissions.IsGranted(membership.BoutiqueRole, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
