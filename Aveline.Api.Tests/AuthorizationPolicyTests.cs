using System.Security.Claims;
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
}
