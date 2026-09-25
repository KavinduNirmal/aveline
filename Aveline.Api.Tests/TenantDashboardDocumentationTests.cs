using System.Text.RegularExpressions;

namespace Aveline.Api.Tests;

/// <summary>
/// Tenant-dashboard slice T7. The statistics catalog opens with the rule that **nothing is exposed
/// by the API unless it appears in the catalog and in <c>docs/api/openapi.yaml</c>**
/// (<c>docs/backend/statistics-catalog.md</c> §1). Until this test, that rule was a promise: the
/// endpoints shipped, the docs were edited by hand, and nothing mechanical connected the two.
///
/// This is a **source scan plus a documentation scan**, deliberately. A minimal-API route is not
/// reachable from a unit test without standing up the whole host and its Postgres/Redis
/// dependencies, and the failure mode being guarded against is exactly "a route was added and one
/// of its two documents was forgotten". The list below is the frozen T7 contract: each entry names
/// the route's method, its normalised path, the file that maps it and a literal that file must
/// still contain. If a route is renamed or deleted, the source half fails; if a document loses its
/// row, the docs half fails.
/// </summary>
public class TenantDashboardDocumentationTests
{
    private const string ApiPrefix = "/api/v1/orgs/{organizationId}";

    /// <summary>Collapses a route token's constraining suffix: <c>{id:guid}</c> becomes <c>{id}</c>.</summary>
    private static readonly Regex GuidToken =
        new(@"\{([A-Za-z0-9_]+):[^}]+\}", RegexOptions.Compiled);

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

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var parts = new List<string> { RepositoryRoot };
        parts.AddRange(relativeParts);
        var path = Path.Combine(parts.ToArray());
        Assert.True(File.Exists(path), $"Expected repository file to exist: {path}");
        return File.ReadAllText(path);
    }

    private static string Normalize(string text) => GuidToken.Replace(text.Replace("\r\n", "\n"), "{$1}");

    /// <summary>
    /// Method, normalised path, source file, and a route literal that file must still contain.
    /// E-1…E-13 of the plan's §5.1 endpoint table.
    /// </summary>
    public static TheoryData<string, string, string, string> NewRoutes() => new()
    {
        // E-13 — the member-level reduced takings read.
        { "GET", $"{ApiPrefix}/dashboard/takings", "Aveline.Api/Modules/Commerce/Endpoints/DashboardEndpoints.cs", "\"/dashboard/takings\"" },
        // E-1…E-3 — the KPI strip, the revenue series and the top items.
        { "GET", $"{ApiPrefix}/dashboard/summary", "Aveline.Api/Modules/Commerce/Endpoints/DashboardEndpoints.cs", "\"/summary\"" },
        { "GET", $"{ApiPrefix}/dashboard/revenue-series", "Aveline.Api/Modules/Commerce/Endpoints/DashboardEndpoints.cs", "\"/revenue-series\"" },
        { "GET", $"{ApiPrefix}/dashboard/top-items", "Aveline.Api/Modules/Commerce/Endpoints/DashboardEndpoints.cs", "\"/top-items\"" },
        // E-4/E-5 — the income register and the per-kind breakdown.
        { "GET", $"{ApiPrefix}/income/ledger", "Aveline.Api/Modules/Commerce/Endpoints/IncomeEndpoints.cs", "\"/ledger\"" },
        { "GET", $"{ApiPrefix}/income/accounts", "Aveline.Api/Modules/Commerce/Endpoints/IncomeEndpoints.cs", "\"/accounts\"" },
        // E-6…E-9 — customer read-by-id, update, delete and interaction history.
        { "GET", $"{ApiPrefix}/customers/{{customerId}}", "Aveline.Api/Endpoints/CustomerTenantEndpoints.cs", "\"/{customerId:guid}\"" },
        { "PATCH", $"{ApiPrefix}/customers/{{customerId}}", "Aveline.Api/Endpoints/CustomerTenantEndpoints.cs", "\"/{customerId:guid}\"" },
        { "DELETE", $"{ApiPrefix}/customers/{{customerId}}", "Aveline.Api/Endpoints/CustomerTenantEndpoints.cs", "\"/{customerId:guid}\"" },
        { "GET", $"{ApiPrefix}/customers/{{customerId}}/interactions", "Aveline.Api/Endpoints/CustomerTenantEndpoints.cs", "\"/{customerId:guid}/interactions\"" },
        // E-10 — bulk invitations.
        { "POST", $"{ApiPrefix}/invitations/bulk", "Aveline.Api/Endpoints/OrganizationEndpoints.cs", "\"/{organizationId:guid}/invitations/bulk\"" },
        // E-11/E-12 — the top-up catalogue and the billing-period history.
        { "GET", $"{ApiPrefix}/blossoms/top-up-packs", "Aveline.Api/Modules/Billing/Endpoints/BlossomEndpoints.cs", "\"/top-up-packs\"" },
        { "GET", $"{ApiPrefix}/billing/periods", "Aveline.Api/Modules/Billing/Endpoints/OrgUsageEndpoints.cs", "\"/{organizationId:guid}/billing/periods\"" },
    };

    [Theory]
    [MemberData(nameof(NewRoutes))]
    public void EveryNewRoute_IsStillMappedInTheEndpointSource(
        string method,
        string path,
        string sourceFile,
        string sourceLiteral)
    {
        var source = ReadRepoFile(sourceFile.Split('/'));
        Assert.Contains(sourceLiteral, source);

        // The method must map too, or the docs would describe a verb the source does not offer.
        var mapCall = "Map" + char.ToUpperInvariant(method[0]) + method[1..].ToLowerInvariant() + "(";
        Assert.Contains(mapCall, source);
    }

    [Theory]
    [MemberData(nameof(NewRoutes))]
    public void EveryNewRoute_AppearsInTheApiReadme(string method, string path, string _, string __)
    {
        var readme = Normalize(ReadRepoFile("docs", "api", "README.md"));

        var matchingLine = readme
            .Split('\n')
            .FirstOrDefault(line => line.Contains(path, StringComparison.Ordinal)
                                 && line.Contains($"`{method}`", StringComparison.Ordinal));

        Assert.True(
            matchingLine is not null,
            $"docs/api/README.md has no `{method}` row for {path}. A route that ships without its "
            + "catalogue row is, by the catalog's own rule, not exposed.");
    }

    [Theory]
    [MemberData(nameof(NewRoutes))]
    public void EveryNewRoute_AppearsInTheOpenApiSpec(string method, string path, string _, string __)
    {
        var spec = Normalize(ReadRepoFile("docs", "api", "openapi.yaml"));
        var marker = $"\n  {path}:\n";
        var start = spec.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(start >= 0, $"docs/api/openapi.yaml has no path item for {path}.");

        // Read the path item's own block: from its key to the next top-level path key.
        var body = spec[(start + marker.Length)..];
        var next = body.IndexOf("\n  /", StringComparison.Ordinal);
        var block = next >= 0 ? body[..next] : body;

        Assert.True(
            block.Contains($"\n    {method.ToLowerInvariant()}:", StringComparison.Ordinal),
            $"docs/api/openapi.yaml declares {path} but not its `{method}` operation.");
    }

    /// <summary>
    /// S-57…S-63 are the tenant-dashboard statistics entries. Each was landed in the slice that
    /// shipped its endpoint rather than in the docs slice, because S-56's mistake was documenting a
    /// route before it existed; this asserts they are all present and none was renumbered.
    /// </summary>
    [Theory]
    [InlineData(57, "tenantDashboardSummary")]
    [InlineData(58, "tenantRevenueTimeseries")]
    [InlineData(59, "tenantTopItems")]
    [InlineData(60, "boutiqueIncomeLedger")]
    [InlineData(61, "boutiqueIncomeAccounts")]
    [InlineData(62, "tenantBillingPeriods")]
    [InlineData(63, "blossomTopUpPacks")]
    public void EveryTenantStatisticsEntry_IsRegistered(int id, string name)
    {
        var catalog = ReadRepoFile("docs", "backend", "statistics-catalog.md");
        Assert.Contains($"### S-{id} · `{name}`", catalog);
    }

    /// <summary>
    /// The tenant surface is documented where **shipped** endpoints live (Part B), with the old Part
    /// C plan entries pointing at it rather than duplicating a stale specification. This is the one
    /// deliberate deviation from the T7 issue's wording ("the Part C section"): Part C is titled
    /// "Planned endpoints", so documenting live routes there would reintroduce exactly the
    /// documented-but-unimplemented defect the catalog warns against.
    /// </summary>
    [Fact]
    public void TheTenantDashboardSections_LiveInPartB()
    {
        var readme = ReadRepoFile("docs", "api", "README.md");

        Assert.Contains("### B.20 Tenant customer surface", readme);
        Assert.Contains("### B.21 Boutique income", readme);
        Assert.Contains("### B.22 Tenant dashboard KPIs", readme);
        Assert.Contains("### B.23 Tenant billing reads", readme);
        Assert.Contains("Status: shipped, and now documented in Part B.", readme);
    }
}
