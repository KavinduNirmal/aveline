using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Aveline.Api.Authorization;

/// <summary>
/// Evaluates <see cref="PermissionRequirement"/> by intersecting the caller's roles
/// with the roles allowed for the requested permission.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userRoles = context.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value);

        var granted = userRoles.Any(role => Permissions.IsGranted(role, requirement.Permission));

        if (granted)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
