using Aveline.Api.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Aveline.Api.Configurations;

/// <summary>
/// Registers role-based and permission-based authorization policies.
/// </summary>
public static class AuthorizationConfiguration
{
    /// <summary>Role-based policy names.</summary>
    public const string AssociatesPolicy = "Associates";
    public const string ManagersPolicy = "Managers";
    public const string OwnersPolicy = "Owners";

    public static IServiceCollection AddAvelineAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            // Role-based policies.
            options.AddPolicy(AssociatesPolicy, p => p.RequireRole(Roles.StaffAccess));

            options.AddPolicy(ManagersPolicy, p => p.RequireRole(Roles.ManagementAccess));

            options.AddPolicy(OwnersPolicy, p => p.RequireRole(Roles.OwnershipAccess));

            // Permission-based policies (one per permission in the catalog).
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, p => p.Requirements.Add(new PermissionRequirement(permission)));
            }
        });

        return services;
    }
}
