using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// Tenant-dashboard slice T0a. The new org-scoped policies must carry an
/// <see cref="OrganizationScopeRequirement"/> so the <c>organizationId</c> route value is
/// matched against the database membership, rather than authorising on the JWT's possibly-stale
/// role claims. `BoutiqueMemberPolicy` is the permission-free member gate: a route that only
/// needs "an active member of this organisation" must not borrow `catalog:view` to say so.
/// </summary>
public class TenantDashboardPolicyTests
{
    private static readonly ServiceProvider Services = BuildServices();

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvelineAuthorization();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(AuthorizationConfiguration.BoutiqueCatalogManagePolicy, Permissions.CatalogManage)]
    [InlineData(AuthorizationConfiguration.BoutiqueCustomerManagePolicy, Permissions.CustomersManage)]
    [InlineData(AuthorizationConfiguration.BoutiqueTeamManagePolicy, Permissions.TeamManage)]
    [InlineData(AuthorizationConfiguration.BoutiqueOrderManagePolicy, Permissions.OrdersManage)]
    [InlineData(AuthorizationConfiguration.BoutiqueReportsViewPolicy, Permissions.ReportsView)]
    public async Task NewNamedOrgScopedPolicies_CarryTheirPermissionRequirement(
        string policyName,
        string permission)
    {
        var provider = Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetPolicyAsync(policyName);

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy!.Requirements.OfType<OrganizationScopeRequirement>());
        Assert.Equal(permission, requirement.Permission);
    }

    [Fact]
    public async Task BoutiqueMemberPolicy_RequiresMembershipButNoPermission()
    {
        // TD11 option (b): a permission-free member policy, so the next route that only needs
        // "an active member" does not have to choose between "catalog" and "order".
        var provider = Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetPolicyAsync(AuthorizationConfiguration.BoutiqueMemberPolicy);

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy!.Requirements.OfType<OrganizationScopeRequirement>());
        Assert.Null(requirement.Permission);
    }

    [Fact]
    public async Task TheNewPermissions_DoNotAccidentallyRegisterAsBareOrgScopedPolicies()
    {
        // The auto-registration loop adds a bare permission policy for every name in the
        // catalog. Those carry no organization scope, which is exactly why a tenant route must
        // name the org-scoped policy instead. This is the standing guard against a route
        // authorising on JWT claims by accident.
        var provider = Services.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var permission in new[]
                 {
                     Permissions.CustomersManage,
                     Permissions.TeamManage,
                     Permissions.OrdersManage,
                 })
        {
            var bare = await provider.GetPolicyAsync(permission);
            Assert.NotNull(bare);
            Assert.Empty(bare!.Requirements.OfType<OrganizationScopeRequirement>());
        }
    }

    [Theory]
    [InlineData(AuthorizationConfiguration.BoutiqueCatalogManagePolicy)]
    [InlineData(AuthorizationConfiguration.BoutiqueCustomerManagePolicy)]
    [InlineData(AuthorizationConfiguration.BoutiqueTeamManagePolicy)]
    [InlineData(AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
    [InlineData(AuthorizationConfiguration.BoutiqueReportsViewPolicy)]
    [InlineData(AuthorizationConfiguration.BoutiqueMemberPolicy)]
    public async Task EveryNewTenantPolicy_AcceptsBearerOrApiKey(string policyName)
    {
        // The org-scoped policies are reachable through both schemes; the handlers then
        // evaluate the membership (bearer) or the granted scope (API key).
        var provider = Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetPolicyAsync(policyName);

        Assert.NotNull(policy);
        Assert.Contains(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            policy!.AuthenticationSchemes);
        Assert.Contains(
            Aveline.Api.Modules.ApiAccess.Authentication.ApiKeyAuthenticationHandler.SchemeName,
            policy.AuthenticationSchemes);
    }
}
