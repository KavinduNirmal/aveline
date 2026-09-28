using System.Text.RegularExpressions;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;

namespace Aveline.Api.Tests;

/// <summary>
/// Tenant-dashboard slice T0a. The eight team routes move off
/// <see cref="AuthorizationConfiguration.BoutiqueMembershipManagePolicy"/>
/// (<c>settings:manage</c>, which also reaches Integrations and its gateway credentials) onto
/// <see cref="AuthorizationConfiguration.BoutiqueTeamManagePolicy"/> (<c>team:manage</c>). The two
/// organisation settings routes stay where they are. A source scan is the honest assertion here:
/// a minimal-API route attribute is not reachable from a unit test, and the failure mode this
/// guards against is exactly "nobody noticed which policy the route carried".
/// </summary>
public class TeamRoutePolicyTests
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

    private static string OrganizationEndpoints()
        => File.ReadAllText(Path.Combine(
            RepositoryRoot, "Aveline.Api", "Endpoints", "OrganizationEndpoints.cs"));

    /// <summary>
    /// Splits the file at each route declaration and reads the first <c>RequireAuthorization</c>
    /// in that route's own chunk, which is how a chained minimal-API declaration reads.
    /// </summary>
    private static IReadOnlyList<(string Route, string Policy)> RoutePolicies()
    {
        var source = OrganizationEndpoints();
        var declarations = Regex.Split(source, @"(?=orgGroup\.Map(?:Get|Post|Patch|Delete|Put)\()");

        var result = new List<(string, string)>();
        foreach (var chunk in declarations)
        {
            var route = Regex.Match(chunk, @"orgGroup\.Map\w+\(""(?<route>[^""]*)""");
            if (!route.Success) continue;

            var policy = Regex.Match(
                chunk,
                @"RequireAuthorization\(\s*(?:AuthorizationConfiguration\.)?(?<policy>[A-Za-z]+)\s*\)");
            if (!policy.Success) continue;

            result.Add((route.Groups["route"].Value, policy.Groups["policy"].Value));
        }

        return result;
    }

    [Theory]
    [InlineData("/{organizationId:guid}/invitations", 2)]
    [InlineData("/{organizationId:guid}/invitations/{invitationId:guid}/revoke", 1)]
    [InlineData("/{organizationId:guid}/members/{userId:guid}/suspend", 1)]
    [InlineData("/{organizationId:guid}/members/{userId:guid}/activate", 1)]
    [InlineData("/{organizationId:guid}/members", 1)]
    public void TeamRoutes_AreGatedByTeamManage(string route, int expectedCount)
    {
        var matches = RoutePolicies().Where(p => p.Route == route).ToList();

        Assert.Equal(expectedCount, matches.Count);
        Assert.All(matches, m =>
            Assert.Equal(nameof(AuthorizationConfiguration.BoutiqueTeamManagePolicy), m.Policy));
    }

    [Fact]
    public void MemberMutationRoutes_AreGatedByTeamManage()
    {
        // `PATCH …/members/{userId}` (role change) and `DELETE …/members/{userId}` (removal)
        // share one route template with different verbs, so they are counted together.
        var matches = RoutePolicies()
            .Where(p => p.Route == "/{organizationId:guid}/members/{userId:guid}")
            .ToList();

        Assert.Equal(2, matches.Count);
        Assert.All(matches, m =>
            Assert.Equal(nameof(AuthorizationConfiguration.BoutiqueTeamManagePolicy), m.Policy));
    }

    [Fact]
    public void NoInvitationOrMemberRoute_StillCarriesMembershipManage()
    {
        var leftovers = RoutePolicies()
            .Where(p => p.Policy == nameof(AuthorizationConfiguration.BoutiqueMembershipManagePolicy))
            .Select(p => p.Route)
            .ToList();

        // The two organisation settings routes are the only survivors of the split.
        Assert.Equal(2, leftovers.Count);
        Assert.Contains("/{organizationId:guid}", leftovers);
        Assert.Contains("/{organizationId:guid}/settings", leftovers);
    }

    [Fact]
    public void TeamManagePolicy_ComesFromTeamManageNotSettingsManage()
    {
        // The separation is the point of the permission: `settings:manage` reaching Integrations
        // is what made granting it to a manager the wrong answer to "may a manager manage staff".
        var policy = typeof(AuthorizationConfiguration)
            .GetField(nameof(AuthorizationConfiguration.BoutiqueTeamManagePolicy))!
            .GetRawConstantValue() as string;
        Assert.Equal("BoutiqueTeamManage", policy);
        Assert.True(Permissions.IsGranted(Roles.BoutiqueManager, Permissions.TeamManage));
        Assert.False(Permissions.IsGranted(Roles.BoutiqueManager, Permissions.SettingsManage));
    }
}
