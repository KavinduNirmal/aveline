using System.Text.RegularExpressions;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;

namespace Aveline.Api.Tests;

/// <summary>
/// T1 / F-2. The whole <c>/orgs/{organizationId}/catalog</c> group carried
/// <c>BoutiqueAccessPolicy</c> (= <c>catalog:view</c>), including every write route, and
/// <c>catalog:manage</c> was enforced on **zero** routes in the codebase. The defect was not that
/// the wrong policy was chosen: it is that **no policy was named per route at all**, so the answer
/// to "what does this route need?" was `catalog:view` by inheritance.
///
/// Strategy TD12 decided the split: **four managed** routes (create, edit, publish, delete) and
/// **ten operational** ones (search, label, scan, vision, matches, compose, sourcing, upload) that
/// stay at member level, because a blanket <c>catalog:manage</c> would have taken the camera, the
/// label printer and the AI analysis away from <c>org:boutique_staff</c>.
/// </summary>
public class CatalogWriteAuthorizationTests
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

    private static string Endpoints()
        => File.ReadAllText(Path.Combine(
            RepositoryRoot, "Aveline.Api", "Endpoints", "CatalogEndpoints.cs"));

    /// <summary>
    /// The number of catalog-manage / member policy applications, and their exact call text.
    /// Counting the *applications* rather than parsing declarations is deliberate: the failure this
    /// guards against is "a route silently inherited the group policy", and one count per policy
    /// answers it directly.
    /// </summary>
    private static int CountPolicy(string policy)
        => Regex.Matches(
            Endpoints(),
            $@"\.RequireAuthorization\(AuthorizationConfiguration\.{policy}\)").Count;

    [Fact]
    public void TheGroupPolicyIsStillMemberLevel()
    {
        // Reads inherit `catalog:view` from the group and that is not changed: the per-route
        // attribute composes on top, which is house style (ConversationEndpoints).
        Assert.Contains("BoutiqueAccessPolicy", Endpoints());
    }

    [Fact]
    public void ExactlyFourRoutesRequireCatalogManage()
    {
        Assert.Equal(4, CountPolicy(nameof(AuthorizationConfiguration.BoutiqueCatalogManagePolicy)));
    }

    [Fact]
    public void ExactlyTenRoutesUseThePermissionFreeMemberGate()
    {
        Assert.Equal(10, CountPolicy(nameof(AuthorizationConfiguration.BoutiqueMemberPolicy)));
    }

    [Theory]
    [InlineData("Post", "/items")]
    [InlineData("Put", "/items/{itemId:guid}")]
    [InlineData("Patch", "/items/{itemId:guid}/status")]
    [InlineData("Delete", "/items/{itemId:guid}")]
    public void EachManagedRouteNamesTheCatalogManagePolicy(string method, string route)
    {
        Assert.Matches(
            new Regex(
                $@"group\.Map{method}\(""{Regex.Escape(route)}""[\s\S]{{0,6000}}?\.RequireAuthorization\(AuthorizationConfiguration\.BoutiqueCatalogManagePolicy\)"),
            Endpoints());
    }

    [Theory]
    [InlineData("Post", "/items/search")]
    [InlineData("Post", "/qr/generate")]
    [InlineData("Post", "/items/scan-qr")]
    [InlineData("Post", "/qr/scan")]
    [InlineData("Post", "/analyze-image")]
    [InlineData("Post", "/items/{itemId:guid}/matches/generate")]
    [InlineData("Post", "/lookbooks/compose")]
    [InlineData("Post", "/sourcing")]
    [InlineData("Patch", "/sourcing/{id:guid}/status")]
    [InlineData("Post", "/images/upload")]
    public void EachOperationalRouteNamesTheMemberGate(string method, string route)
    {
        // These are the tools a staff-facing flow uses. Gating them on `catalog:manage` would 403
        // the camera; they take the permission-free member gate instead, which is what
        // `BoutiqueMemberPolicy` exists for.
        Assert.Matches(
            new Regex(
                $@"group\.Map{method}\(""{Regex.Escape(route)}""[\s\S]{{0,6000}}?\.RequireAuthorization\(AuthorizationConfiguration\.BoutiqueMemberPolicy\)"),
            Endpoints());
    }

    [Fact]
    public void CatalogManage_IsHeldByManagementRolesOnly()
    {
        Assert.True(Permissions.IsGranted(Roles.BoutiqueManager, Permissions.CatalogManage));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueSupervisor, Permissions.CatalogManage));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueOwner, Permissions.CatalogManage));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.CatalogManage));
    }

    [Fact]
    public void TheImageRoute_KeepsAllowAnonymousWithItsReasonRecorded()
    {
        // F-7, answered: catalog imagery is public and will be hosted on Cloudinary. The attribute
        // stays, and the reason lives beside it rather than being inherited silently.
        var source = Endpoints();
        var index = source.IndexOf("images/{imageId:guid}", StringComparison.Ordinal);
        Assert.True(index > 0, "the image route must exist");

        var tail = source[index..Math.Min(source.Length, index + 2000)];
        Assert.Contains(".AllowAnonymous()", tail);
        Assert.Matches(
            new Regex(@"public imagery|Cloudinary", RegexOptions.IgnoreCase),
            tail);
    }
}
