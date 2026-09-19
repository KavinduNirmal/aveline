using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies the role-based and permission-based authorization policies registered
/// by <see cref="AuthorizationConfiguration.AddAvelineAuthorization"/>.
/// Each test builds its own service provider, so tests are independent.
/// </summary>
public class AuthorizationPolicyTests
{
    private static readonly ServiceProvider Services = BuildServices();
    private static readonly IAuthorizationService Authorization =
        Services.GetRequiredService<IAuthorizationService>();

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvelineAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    private static ClaimsPrincipal PrincipalWithRawRoles(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim("user_role", r)), "test"));

    private static bool IsAuthorized(ClaimsPrincipal principal, string policy) =>
        Authorization.AuthorizeAsync(principal, null, policy).GetAwaiter().GetResult().Succeeded;

    [Theory]
    [InlineData("staff")]
    [InlineData("customer_relations")]
    [InlineData("moderator")]
    [InlineData("admin")]
    [InlineData("owner")]
    [InlineData("org:boutique_staff")]
    [InlineData("org:boutique_manager")]
    [InlineData("org:boutique_supervisor")]
    [InlineData("org:boutique_owner")]
    public void AssociatesPolicy_Allows_AnyStaffRole(string role)
    {
        Assert.True(IsAuthorized(Principal(role), AuthorizationConfiguration.AssociatesPolicy));
    }

    [Fact]
    public void AssociatesPolicy_Denies_PrincipalWithoutRoles()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        Assert.False(IsAuthorized(principal, AuthorizationConfiguration.AssociatesPolicy));
    }

    [Fact]
    public void AssociatesPolicy_Allows_BoutiqueStaff()
    {
        Assert.True(IsAuthorized(Principal("org:boutique_staff"), AuthorizationConfiguration.AssociatesPolicy));
    }

    [Theory]
    [InlineData("moderator")]
    [InlineData("admin")]
    [InlineData("owner")]
    [InlineData("org:boutique_manager")]
    [InlineData("org:boutique_supervisor")]
    [InlineData("org:boutique_owner")]
    public void ManagersPolicy_Allows_ManagerAndAbove(string role)
    {
        Assert.True(IsAuthorized(Principal(role), AuthorizationConfiguration.ManagersPolicy));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("customer_relations")]
    [InlineData("org:boutique_staff")]
    public void ManagersPolicy_Denies_StaffOnlyRoles(string role)
    {
        Assert.False(IsAuthorized(Principal(role), AuthorizationConfiguration.ManagersPolicy));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("org:boutique_owner")]
    public void OwnersPolicy_Allows_OnlyOwners(string role)
    {
        Assert.True(IsAuthorized(Principal(role), AuthorizationConfiguration.OwnersPolicy));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("moderator")]
    [InlineData("org:boutique_supervisor")]
    [InlineData("org:boutique_manager")]
    public void OwnersPolicy_Denies_NonOwners(string role)
    {
        Assert.False(IsAuthorized(Principal(role), AuthorizationConfiguration.OwnersPolicy));
    }

    [Fact]
    public void Raw_UserRole_Claim_WithoutPromotion_IsNotAuthorized()
    {
        // Simulates a token before RoleClaimNormalizer runs: only the raw "user_role"
        // claim is present, so policy checks (which read ClaimTypes.Role) must fail.
        Assert.False(IsAuthorized(PrincipalWithRawRoles("staff"), AuthorizationConfiguration.AssociatesPolicy));
    }

    [Fact]
    public void ApprovalsApprove_Allows_BoutiqueSupervisor()
    {
        Assert.True(IsAuthorized(Principal("org:boutique_supervisor"), "approvals:approve"));
    }

    [Fact]
    public void ApprovalsApprove_Denies_Associate()
    {
        Assert.False(IsAuthorized(Principal("org:boutique_manager"), "approvals:approve"));
    }

    [Fact]
    public void PaymentsRefund_Allows_Owner()
    {
        Assert.True(IsAuthorized(Principal("org:boutique_owner"), "payments:refund"));
    }

    [Fact]
    public void PaymentsRefund_Denies_Manager()
    {
        Assert.False(IsAuthorized(Principal("org:boutique_supervisor"), "payments:refund"));
    }

    [Fact]
    public void CatalogView_Allows_AllStaff()
    {
        Assert.True(IsAuthorized(Principal("staff"), "catalog:view"));
    }

    [Fact]
    public void SettingsManage_Denies_Manager()
    {
        Assert.False(IsAuthorized(Principal("org:boutique_supervisor"), "settings:manage"));
    }

    [Theory]
    [InlineData(AuthorizationConfiguration.BoutiqueCustomerAccessPolicy, Permissions.CustomersView)]
    [InlineData(AuthorizationConfiguration.BoutiqueBillingSelfViewPolicy, Permissions.BillingViewSelf)]
    public void NamedOrgScopedPolicies_CarryAnOrganizationScopeRequirement(
        string policyName,
        string permission)
    {
        // A bare per-permission policy (registered for every name in the catalog)
        // has no organization scope: it would authorize on the JWT's possibly-stale
        // role claims and never consult the `organizationId` route value. Tenant
        // routes must therefore name a policy that carries the requirement.
        var provider = Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = provider.GetPolicyAsync(policyName).GetAwaiter().GetResult();

        Assert.NotNull(policy);
        var requirement = Assert.Single(
            policy!.Requirements.OfType<OrganizationScopeRequirement>());
        Assert.Equal(permission, requirement.Permission);
    }

    [Theory]
    [InlineData(AuthorizationConfiguration.StatsSystemPolicy, Permissions.StatsSystem)]
    [InlineData(AuthorizationConfiguration.AuditViewPolicy, Permissions.AuditView)]
    [InlineData(AuthorizationConfiguration.PricingAdminReadPolicy, Permissions.PricingView)]
    public void TeamOnlyPolicies_CarryTheirPermissionRequirement(
        string policyName,
        string permission)
    {
        // A9 B2: `stats:system`, `audit:view` and `pricing:view` were registered as bare
        // permission policies but no endpoint referenced them, while the routes that
        // logically own them were role-only. The requirement is added alongside the role
        // guard so the catalogue and the wire agree.
        var provider = Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = provider.GetPolicyAsync(policyName).GetAwaiter().GetResult();

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(permission, requirement.Permission);
    }

    [Theory]
    [InlineData(AuthorizationConfiguration.StatsSystemPolicy)]
    [InlineData(AuthorizationConfiguration.AuditViewPolicy)]
    [InlineData(AuthorizationConfiguration.PricingAdminReadPolicy)]
    public void TeamOnlyPolicies_AdmitOwnerAndAdmin(string policyName)
    {
        Assert.True(IsAuthorized(Principal("owner"), policyName));
        Assert.True(IsAuthorized(Principal("admin"), policyName));
    }

    [Theory]
    [InlineData(AuthorizationConfiguration.StatsSystemPolicy)]
    [InlineData(AuthorizationConfiguration.AuditViewPolicy)]
    [InlineData(AuthorizationConfiguration.PricingAdminReadPolicy)]
    public void TeamOnlyPolicies_RefuseEveryOtherRole(string policyName)
    {
        Assert.False(IsAuthorized(Principal("moderator"), policyName));
        Assert.False(IsAuthorized(Principal("staff"), policyName));
        Assert.False(IsAuthorized(Principal("customer_relations"), policyName));
        Assert.False(IsAuthorized(Principal("org:boutique_owner"), policyName));
        Assert.False(IsAuthorized(Principal("org:boutique_manager"), policyName));
        Assert.False(IsAuthorized(Principal("org:boutique_supervisor"), policyName));
        Assert.False(IsAuthorized(Principal("org:boutique_staff"), policyName));
        Assert.False(IsAuthorized(
            new ClaimsPrincipal(new ClaimsIdentity("test")), policyName));
    }
}
