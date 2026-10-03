using System.Security.Claims;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Authorization;

/// <summary>
/// Demands that the caller is staff somewhere: a platform (Aveline team) role, or an active
/// organization membership.
/// </summary>
/// <remarks>
/// Exists for routes that have no <c>organizationId</c> in their path and therefore cannot use
/// <see cref="OrganizationScopeRequirement"/>. It replaces asking such a route for
/// <c>RequireRole(Roles.StaffAccess)</c>: the boutique half of that list can only arrive through
/// the Clerk <c>org_role</c> claim, and this deployment creates no Clerk organizations, so the
/// claim is always empty and those routes admitted platform staff while refusing every boutique
/// owner. Membership is read from the database, never the org claims.
/// </remarks>
public sealed class StaffMembershipRequirement : IAuthorizationRequirement;

/// <summary>
/// Satisfies <see cref="StaffMembershipRequirement"/> from a platform role held in the token, or
/// from an active <see cref="Modules.Organizations.Models.OrganizationMembership"/> row.
/// </summary>
public sealed class StaffMembershipAuthorizationHandler
    : AuthorizationHandler<StaffMembershipRequirement>
{
    /// <summary>
    /// The platform (Aveline team) half of <see cref="Roles.StaffAccess"/>. The boutique half is
    /// deliberately absent rather than listed: it is reachable only through the Clerk
    /// <c>org_role</c> claim, and the membership check below is its canonical replacement.
    /// </summary>
    private static readonly string[] PlatformStaffRoles =
        [.. Roles.StaffAccess.Where(role => !role.StartsWith("org:", StringComparison.Ordinal))];

    private readonly IHttpContextAccessor _httpContextAccessor;

    public StaffMembershipAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StaffMembershipRequirement requirement)
    {
        if (PlatformStaffRoles.Any(context.User.IsInRole))
        {
            context.Succeed(requirement);
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
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

        if (await organizationRepository.UserHasActiveMembershipAsync(user.Id))
        {
            context.Succeed(requirement);
        }
    }
}
