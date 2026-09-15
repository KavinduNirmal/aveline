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
    /// Org-scoped policy for the conversation inbox ("The Salon"): requires an active
    /// membership in the target organization whose role grants <c>conversations:view</c>
    /// (all boutique staff roles).
    /// </summary>
    public const string BoutiqueConversationAccessPolicy = "BoutiqueConversationAccess";

    /// <summary>
    /// Policy for internal service-to-service calls using X-Internal-Token header (ADR-009).
    /// </summary>
    public const string InternalServicePolicy = "InternalServicePolicy";

    /// <summary>
    /// Org-scoped policy for the Blossom balance, usage and statement: requires an active
    /// membership granting <c>billing:view</c>.
    /// </summary>
    public const string BillingViewPolicy = "BillingView";

    /// <summary>
    /// Org-scoped policy for plan changes and top-up purchases: requires an active
    /// membership granting <c>billing:manage</c>.
    /// </summary>
    public const string BillingManagePolicy = "BillingManage";

    /// <summary>
    /// Policy for the Prometheus scrape endpoint: an internal service token or the
    /// optional <c>Metrics:ScrapeToken</c> bearer token.
    /// </summary>
    public const string MetricsPolicy = "Metrics";

    /// <summary>Org-scoped policy for listing API keys: requires <c>apikeys:view</c>.</summary>
    public const string ApiKeysViewPolicy = "ApiKeysView";

    /// <summary>Org-scoped policy for creating and revoking API keys: requires <c>apikeys:manage</c>.</summary>
    public const string ApiKeysManagePolicy = "ApiKeysManage";

    /// <summary>Org-scoped policy for API-consumption statistics: requires <c>stats:view</c>.</summary>
    public const string StatsViewPolicy = "StatsView";

    /// <summary>Org-scoped policy for agentic statistics: requires <c>stats:view:agent</c>.</summary>
    public const string StatsAgentPolicy = "StatsAgent";

    /// <summary>Team-only policy for system statistics and alerts: requires <c>stats:system</c>.</summary>
    public const string StatsSystemPolicy = "StatsSystem";

    /// <summary>Team-only policy for reading the audit log: requires <c>audit:view</c>.</summary>
    public const string AuditViewPolicy = "AuditView";

    /// <summary>
    /// Team-only policy for the admin pricing read surface (docs/api/README.md §C.1):
    /// the API catalogue states <c>pricing:view</c> is never available to boutique roles,
    /// so the read routes require an Aveline team role rather than the bare permission.
    /// </summary>
    public const string PricingAdminReadPolicy = "PricingAdminRead";

    /// <summary>
    /// An org-scoped policy accepts either a Clerk bearer token or an API key. API-key
    /// principals are evaluated by scope in the authorization handlers (A-10); team-only
    /// policies deliberately omit the API-key scheme.
    /// </summary>
    private static void AllowBearerOrApiKey(AuthorizationPolicyBuilder policy)
    {
        policy.AddAuthenticationSchemes(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            Modules.ApiAccess.Authentication.ApiKeyAuthenticationHandler.SchemeName);
        policy.RequireAuthenticatedUser();
    }

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

            // /metrics accepts either the internal service token or a configured scrape token.
            options.AddPolicy(MetricsPolicy, p =>
            {
                p.AddAuthenticationSchemes(
                    Aveline.Api.Infrastructure.Integrations.InternalTokenAuthenticationHandler.SchemeName,
                    Aveline.Api.Infrastructure.Integrations.ScrapeTokenAuthenticationHandler.SchemeName);
                p.RequireAuthenticatedUser();
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
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.CatalogView));
            });

            options.AddPolicy(BoutiqueMembershipManagePolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.SettingsManage));
            });

            options.AddPolicy(BoutiqueConversationAccessPolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.ConversationsView));
            });

            options.AddPolicy(BillingViewPolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.BillingView));
            });

            options.AddPolicy(BillingManagePolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.BillingManage));
            });

            options.AddPolicy(ApiKeysViewPolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.ApiKeysView));
            });

            options.AddPolicy(ApiKeysManagePolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.ApiKeysManage));
            });

            options.AddPolicy(StatsViewPolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.StatsView));
            });

            options.AddPolicy(StatsAgentPolicy, p =>
            {
                AllowBearerOrApiKey(p);
                p.AddRequirements(new OrganizationScopeRequirement(Permissions.StatsViewAgent));
            });

            // Team-only policies: an API key may never reach these, so they only accept
            // the default bearer scheme.
            options.AddPolicy(StatsSystemPolicy, p => p.RequireRole(Roles.Owner, Roles.Admin));

            options.AddPolicy(AuditViewPolicy, p => p.RequireRole(Roles.Owner, Roles.Admin));

            options.AddPolicy(PricingAdminReadPolicy, p => p.RequireRole(Roles.Owner, Roles.Admin));

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
