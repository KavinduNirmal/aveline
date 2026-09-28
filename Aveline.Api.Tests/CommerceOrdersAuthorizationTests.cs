using System.Text.RegularExpressions;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;

namespace Aveline.Api.Tests;

/// <summary>
/// T1 / F-1. <c>OrdersController</c> and <c>BusinessRulesController</c> carried **no
/// <c>[Authorize]</c> at all** and used the route token <c>{orgId:guid}</c>, which the
/// organization-scope handler never reads (it matches <c>organizationId</c> exactly). The
/// combination meant any authenticated caller — including another boutique's staff — could read
/// and write any organization's orders and business rules through the authenticated-only fallback
/// policy.
///
/// A controller attribute is not reachable from a unit test, so this is a source scan. That is the
/// honest assertion: the defect was precisely "nobody noticed what the attribute said".
/// </summary>
public class CommerceOrdersAuthorizationTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null
                   && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return directory!.FullName;
        }
    }

    private static string Controller(string name)
        => File.ReadAllText(Path.Combine(
            RepositoryRoot, "Aveline.Api", "Modules", "Commerce", "Controllers", name));

    [Fact]
    public void OrdersController_UsesTheOrganizationIdRouteToken()
    {
        // The scope handler reads RouteValues["organizationId"] and nothing else, so a policy on a
        // route declared {orgId} would deny every caller rather than authorise the right one.
        var source = Controller("OrdersController.cs");
        Assert.Contains("{organizationId:guid}", source);
        Assert.DoesNotContain("{orgId:guid}", source);
        Assert.DoesNotContain("Guid orgId", source);
    }

    [Fact]
    public void BusinessRulesController_UsesTheOrganizationIdRouteToken()
    {
        var source = Controller("BusinessRulesController.cs");
        Assert.Contains("{organizationId:guid}", source);
        Assert.DoesNotContain("{orgId:guid}", source);
        Assert.DoesNotContain("Guid orgId", source);
    }

    [Fact]
    public void OrdersController_IsAuthorised()
    {
        var source = Controller("OrdersController.cs");
        Assert.Matches(new Regex(@"\[Authorize"), source);
    }

    [Fact]
    public void BusinessRulesController_IsAuthorised()
    {
        var source = Controller("BusinessRulesController.cs");
        Assert.Matches(new Regex(@"\[Authorize"), source);
    }

    [Fact]
    public void OrderReadsAndCreate_AreMemberLevel()
    {
        // TD11: a counter associate must see and create the order they are serving. `catalog:view`
        // is *not* the right gate — it is held by every role and says nothing about orders.
        var source = Controller("OrdersController.cs");
        Assert.Contains("BoutiqueMemberPolicy", source);
        Assert.DoesNotContain("BoutiqueAccessPolicy", source);
    }

    [Fact]
    public void OrderWritesAndBusinessRuleWrites_RequireOrdersManage()
    {
        // Q8: staff may not change order status, cancel an order or edit business rules.
        // `BoutiqueAccess` (= `catalog:view`) would have granted all of it to every role.
        foreach (var name in new[] { "OrdersController.cs", "BusinessRulesController.cs" })
        {
            var source = Controller(name);
            Assert.Contains("BoutiqueOrderManagePolicy", source);
            Assert.DoesNotContain("BoutiqueAccessPolicy", source);
        }
    }

    [Fact]
    public void OrdersManage_IsNotHeldByStaff()
    {
        Assert.True(Permissions.IsGranted(Roles.BoutiqueManager, Permissions.OrdersManage));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueSupervisor, Permissions.OrdersManage));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueOwner, Permissions.OrdersManage));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.OrdersManage));
    }

    /// <summary>
    /// F-6. <c>GET /orgs/{organizationId}/usage</c> was gated by <c>BoutiqueAccess</c> =
    /// <c>catalog:view</c>, contradicting its own doc comment ("Owner/manager-visible"). A usage
    /// read is a *billing* read, so it takes the billing self-service policy.
    /// </summary>
    [Fact]
    public void OrgUsageRoute_IsGatedByTheBillingSelfViewPolicy()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot, "Aveline.Api", "Modules", "Billing", "Endpoints", "OrgUsageEndpoints.cs"));
        Assert.Contains("BoutiqueBillingSelfViewPolicy", source);
        Assert.DoesNotContain("BoutiqueAccessPolicy", source);

        // The change preserves today's effective access: all four boutique roles hold
        // `billing:view:self`, so no role gained or lost the read.
        foreach (var role in new[]
                 {
                     Roles.BoutiqueStaff, Roles.BoutiqueManager,
                     Roles.BoutiqueSupervisor, Roles.BoutiqueOwner,
                 })
        {
            Assert.True(
                Permissions.IsGranted(role, Permissions.BillingViewSelf),
                $"{role} must keep the usage read");
        }
    }
}
