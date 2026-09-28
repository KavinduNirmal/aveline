using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #178 — the permission catalog is the source of truth for authorization. Every
/// new administrative permission must have a registered policy and at least one role
/// grant (M0 acceptance criterion), and boutique roles must never hold money-shaped
/// permissions.
/// </summary>
public class PermissionsCatalogTests
{
    private static readonly string[] AllRoles =
    [
        Roles.Staff,
        Roles.CustomerRelations,
        Roles.Moderator,
        Roles.Admin,
        Roles.Owner,
        Roles.BoutiqueStaff,
        Roles.BoutiqueManager,
        Roles.BoutiqueSupervisor,
        Roles.BoutiqueOwner,
    ];

    private static readonly string[] NewAdministrativePermissions =
    [
        "billing:view",
        "billing:manage",
        "billing:adjust",
        "pricing:view",
        "pricing:manage",
        "pricing:backdate",
        "apikeys:view",
        "apikeys:manage",
        "stats:view",
        "stats:view:agent",
        "stats:system",
        "admin:users:read",
        "admin:users:manage",
        "admin:orgs:read",
        "audit:view",
    ];

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvelineAuthorization();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void All_ContainsEveryNewAdministrativePermission()
    {
        foreach (var permission in NewAdministrativePermissions)
        {
            Assert.Contains(permission, Permissions.All);
        }
    }

    [Fact]
    public async Task EveryPermission_HasARegisteredPolicy()
    {
        var provider = BuildServices().GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var permission in Permissions.All)
        {
            var policy = await provider.GetPolicyAsync(permission);
            Assert.NotNull(policy);
        }
    }

    [Fact]
    public void EveryPermission_HasAtLeastOneRoleGrant()
    {
        foreach (var permission in Permissions.All)
        {
            Assert.NotEmpty(Permissions.RolesGranting(permission));
        }
    }

    [Theory]
    [InlineData("pricing:manage")]
    [InlineData("pricing:backdate")]
    [InlineData("billing:adjust")]
    [InlineData("stats:system")]
    [InlineData("admin:users:manage")]
    [InlineData("audit:view")]
    public void MoneyShapedPermissions_AreNeverGrantedToBoutiqueRoles(string permission)
    {
        Assert.False(Permissions.IsGranted(Roles.BoutiqueOwner, permission));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueManager, permission));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueSupervisor, permission));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueStaff, permission));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public void PricingManage_IsGrantedToTeamLeadership(string role)
    {
        Assert.True(Permissions.IsGranted(role, "pricing:manage"));
    }

    [Fact]
    public void PricingBackdate_IsGrantedOnlyToOwner()
    {
        Assert.True(Permissions.IsGranted(Roles.Owner, "pricing:backdate"));
        Assert.False(Permissions.IsGranted(Roles.Admin, "pricing:backdate"));
    }

    [Theory]
    [InlineData(Roles.BoutiqueOwner)]
    [InlineData(Roles.BoutiqueManager)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public void PricingView_IsGrantedToPricingReaders(string role)
    {
        Assert.True(Permissions.IsGranted(role, "pricing:view"));
    }

    [Theory]
    [InlineData(Roles.BoutiqueStaff)]
    [InlineData(Roles.BoutiqueSupervisor)]
    [InlineData(Roles.Moderator)]
    public void PricingView_IsDeniedToRolesThatDoNotReadPrices(string role)
    {
        Assert.False(Permissions.IsGranted(role, "pricing:view"));
    }

    [Theory]
    [InlineData(Roles.BoutiqueOwner)]
    [InlineData(Roles.BoutiqueManager)]
    [InlineData(Roles.BoutiqueSupervisor)]
    [InlineData(Roles.BoutiqueStaff)]
    public void BoutiqueRoles_PreserveExistingGrants(string role)
    {
        // The grant re-derivation must not accidentally strip existing access.
        Assert.True(Permissions.IsGranted(role, Permissions.CatalogView));
        Assert.True(Permissions.IsGranted(role, Permissions.CustomersView));
        Assert.True(Permissions.IsGranted(role, Permissions.ConversationsView));
    }

    [Theory]
    [InlineData(Roles.BoutiqueOwner)]
    [InlineData(Roles.BoutiqueManager)]
    [InlineData(Roles.BoutiqueSupervisor)]
    [InlineData(Roles.BoutiqueStaff)]
    public void AgentStatisticsPermission_IsNeverGrantedToBoutiqueRoles(string role)
    {
        // A boutique's usage unit is the Blossom. Agent runs, tokens and provider cost are agent
        // internals; the org-scoped statistics routes were removed and no tenant role holds the
        // permission. The platform roles keep it, because the admin console reads the same family.
        Assert.False(Permissions.IsGranted(role, Permissions.StatsViewAgent));
    }

    [Fact]
    public void AgentStatisticsPermission_IsStillHeldByTheTeamRoles()
    {
        Assert.True(Permissions.IsGranted(Roles.Moderator, Permissions.StatsViewAgent));
        Assert.True(Permissions.IsGranted(Roles.Admin, Permissions.StatsViewAgent));
        Assert.True(Permissions.IsGranted(Roles.Owner, Permissions.StatsViewAgent));
    }

    [Fact]
    public void BoutiqueOwner_PreservesSettingsAndRefundGrants()
    {
        Assert.True(Permissions.IsGranted(Roles.BoutiqueOwner, Permissions.SettingsManage));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueOwner, Permissions.PaymentsRefund));
    }

    [Theory]
    [InlineData(Roles.BoutiqueStaff)]
    [InlineData(Roles.BoutiqueManager)]
    [InlineData(Roles.BoutiqueSupervisor)]
    [InlineData(Roles.BoutiqueOwner)]
    public void BillingViewSelf_IsGrantedToEveryOrgRole(string role)
    {
        // Decision D1 (b): an associate may see how many Blossoms the shop has
        // left, through a distinct self-service read rather than the management
        // read.
        Assert.True(Permissions.IsGranted(role, Permissions.BillingViewSelf));
    }

    [Fact]
    public void BillingViewSelf_DoesNotWidenTheManagementRead()
    {
        // The new permission must not be mistaken for a widening of
        // `billing:view`, which reaches usage statements and burn-rate.
        Assert.False(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.BillingView));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueSupervisor, Permissions.BillingView));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueManager, Permissions.BillingView));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueOwner, Permissions.BillingView));
    }

    /// <summary>
    /// The tenant-dashboard slice adds three permissions. They are one atomic change: each
    /// must be in <c>All</c> (or <c>EveryPermission_HasARegisteredPolicy</c> and the client
    /// mirrors fail) and each must name its grants here rather than being inferred.
    /// </summary>
    [Theory]
    [InlineData(Roles.BoutiqueManager)]
    [InlineData(Roles.BoutiqueSupervisor)]
    [InlineData(Roles.BoutiqueOwner)]
    public void TheThreeNewTenantPermissions_AreGrantedToManagementRoles(string role)
    {
        Assert.True(Permissions.IsGranted(role, Permissions.CustomersManage));
        Assert.True(Permissions.IsGranted(role, Permissions.TeamManage));
        Assert.True(Permissions.IsGranted(role, Permissions.OrdersManage));
    }

    [Fact]
    public void TheThreeNewTenantPermissions_AreInTheCatalog()
    {
        Assert.Contains(Permissions.CustomersManage, Permissions.All);
        Assert.Contains(Permissions.TeamManage, Permissions.All);
        Assert.Contains(Permissions.OrdersManage, Permissions.All);
        Assert.Equal(31, Permissions.All.Count);
    }

    [Fact]
    public void BoutiqueStaff_HoldsNoneOfTheNewWritePermissions()
    {
        // Staff may read and may approve a customer order, but may not rewrite a client
        // record, manage staff, or cancel/revise an order.
        Assert.False(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.CustomersManage));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.TeamManage));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.OrdersManage));
    }

    [Fact]
    public void BoutiqueStaff_MayApproveCustomerOrders()
    {
        // Q8: staff approve customer orders. The controller's verb split (reject/revise at
        // `orders:manage`) is what stops the same grant from also cancelling an order.
        Assert.True(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.ApprovalsApprove));
    }

    [Fact]
    public void TeamManage_IsSeparateFromSettingsManage()
    {
        // The whole point of `team:manage`: a manager gains staff management without also
        // gaining Integrations, where the WhatsApp and payment-gateway credentials live.
        Assert.False(Permissions.IsGranted(Roles.BoutiqueManager, Permissions.SettingsManage));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueManager, Permissions.TeamManage));
    }
}
