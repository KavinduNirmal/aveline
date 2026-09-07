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

    /// <summary>Team-level reviewers allowed to review admin access requests.</summary>
    public const string AdminReviewPolicy = "AdminReview";

    /// <summary>
    /// Org-scoped policy name: requires an active canonical membership for the
    /// organization named by the request's <c>organizationId</c> route value and a
    /// membership role granting <c>catalog:view</c>. Denies cross-organization access
    /// even when the caller holds a valid Clerk org claim for another organization.
    /// </summary>
    public const string BoutiqueAccessPolicy = "BoutiqueAccess";

    /// <summary>
    /// Org-scoped policy for membership management: requires an active membership in
    /// the target organization whose role grants <c>settings:manage</c> (boutique owners).
    /// </summary>
    public const string BoutiqueMembershipManagePolicy = "BoutiqueMembershipManage";

    /// <summary>
    /// Policy for internal service-to-service calls using X-Internal-Token header (ADR-009).
    /// </summary>
    public const string InternalServicePolicy = "InternalServicePolicy";

    public static IServiceCollection AddAvelineAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, OrganizationScopeAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            // Internal service-to-service policy
            options.AddPolicy(InternalServicePolicy, p =>
            {
                p.AddAuthenticationSchemes(Aveline.Api.Infrastructure.Integrations.InternalTokenAuthenticationHandler.SchemeName);
                p.RequireAuthenticatedUser();
                p.RequireRole("InternalService");
            });

            // Role-based policies.
            options.AddPolicy(AssociatesPolicy, p => p.RequireRole(Roles.StaffAccess));

            options.AddPolicy(ManagersPolicy, p => p.RequireRole(Roles.ManagementAccess));

            options.AddPolicy(OwnersPolicy, p => p.RequireRole(Roles.OwnershipAccess));

            options.AddPolicy(AdminReviewPolicy, p => p.RequireRole(
                Roles.Moderator, Roles.Admin, Roles.Owner));

            // Organization-scoped policy: authenticated + active membership for the
            // target organization whose boutique role grants catalog:view.
            options.AddPolicy(BoutiqueAccessPolicy, p =>
            {
                p.RequireAuthenticatedUser();
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.CatalogView));
            });

            options.AddPolicy(BoutiqueMembershipManagePolicy, p =>
            {
                p.RequireAuthenticatedUser();
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.SettingsManage));
            });

            // Permission-based policies (one per permission in the catalog).
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, p => p.Requirements.Add(new PermissionRequirement(permission)));
            }

            // New endpoints require authentication unless deliberately marked anonymous.
            options.FallbackPolicy = options.DefaultPolicy;
        });

        return services;
    }
}
