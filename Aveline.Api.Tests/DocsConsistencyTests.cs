using Aveline.Api.Configurations;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 8 (plan §6.8, D7 = A; strategy N-3, R-18). This is the only slice with no
/// execution-level acceptance criterion, so the test is keyed on the **code reference** rather than
/// on a bare phrase: a reworded sentence in a later PR could otherwise restore the defect while
/// still satisfying a phrase grep.
/// </summary>
public class DocsConsistencyTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                directory = directory.Parent;
            }

            directory.Should().NotBeNull();
            return directory!.FullName;
        }
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepositoryRoot, Path.Combine(parts)));

    [Fact]
    public void TheBillingStatisticsClaimNamesTheCodeThatMapsTheRoutes()
    {
        var readme = Read("docs", "backend", "README.md");

        readme.Should().Contain("BillingStatisticsEndpoints.cs", "the correction must cite the code");
        readme.Should().NotContain("No route exists, so\n    they return **404**");
        readme.Should().NotContain("consciously out of scope for the #241 fix");
    }

    [Fact]
    public void TheAgentRollupJobClaimNamesTheJobThatExists()
    {
        var readme = Read("docs", "backend", "README.md");

        readme.Should().Contain("`AgentStatsRollupJob` is present");
        readme.Should().Contain("StatisticsModule");
        readme.Should().NotContain("therefore no `AgentStatsRollupJob`");
    }

    [Fact]
    public void TheDailyRollupClaimNamesTheMethodThatProducesIt()
    {
        var readme = Read("docs", "backend", "README.md");
        var job = Read("Aveline.Api", "Modules", "Statistics", "Jobs", "ApiStatsRollupJob.cs");
        var options = Read("Aveline.Api", "Modules", "Statistics", "Telemetry", "TelemetryOptions.cs");

        readme.Should().Contain("RecomputeDayAsync");
        readme.Should().NotContain("no day rows are produced yet");

        // The code comments must not contradict RunAsync, which calls RecomputeDayAsync at hour 23.
        job.Should().Contain("RecomputeDayAsync");
        job.Should().Contain("Hour→day compaction <b>is</b> produced");
        options.Should().Contain("Day rows are produced by");
    }

    /// <summary>
    /// D7 = A. The cache exists in the code and was written out of the documents as "reversed";
    /// the honest fix is to restore the claim in every document, not to delete it.
    /// </summary>
    [Fact]
    public void TheFifteenSecondOverviewCacheClaimIsRestoredInEveryDocument()
    {
        var apiReadme = Read("docs", "api", "README.md");
        var backendReadme = Read("docs", "backend", "README.md");
        var implementationPlan = Read("docs", "backend", "implementation-plan.md");

        apiReadme.Should().Contain("15 s server-side");
        apiReadme.Should().Contain("SystemStatisticsService");
        apiReadme.Should().NotContain("Not cached server-side");

        backendReadme.Should().Contain("restored, not corrected");
        backendReadme.Should().Contain("SystemStatisticsService.cs");

        implementationPlan.Should().Contain("SystemStatisticsService.cs");
        implementationPlan.Should().NotContain("the Phase 6 shipment: `GET /admin/statistics/system/overview` is composed live");
    }

    [Fact]
    public void TheObservabilityDocumentStatesTheDeferredRegisterAndTheCardinalityRule()
    {
        var doc = Read("docs", "backend", "observability.md");

        // The store-ownership decision, the cardinality prohibition and the deferred register are
        // the three sections whose omission causes an outage or lost work.
        doc.Should().Contain("What each store is authoritative for");
        doc.Should().Contain("Forbidden as a label value");
        doc.Should().Contain("Rotating the scrape token");
        doc.Should().Contain("Adding a dashboard or a panel");
        doc.Should().Contain("deferred register");
        doc.Should().Contain("pg_replication_is_replica");
        doc.Should().Contain("scale-to-zero");
    }

    [Fact]
    public void EveryPrometheusSeriesNamedInTheDocumentationExistsInTheCatalog()
    {
        var doc = Read("docs", "backend", "observability.md");
        var apiReadme = Read("docs", "api", "README.md");
        var exported = MetricsCatalog.All.Select(metric => metric.PrometheusName).ToHashSet(StringComparer.Ordinal);

        foreach (var text in new[] { doc, apiReadme })
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                         text, @"\baveline_(?:api|agent|blossom|eventbus|events|notification|process|queue|db)_[a-z0-9_]+\b"))
            {
                // Documentation writes histograms as `<name>_{bucket,sum,count}`, so a greedy match
                // can carry the separator underscore.
                var series = match.Value.TrimEnd('_');
                var histogramBase = new[] { "_bucket", "_sum", "_count" }
                    .Where(suffix => series.EndsWith(suffix, StringComparison.Ordinal))
                    .Select(suffix => series[..^suffix.Length])
                    .FirstOrDefault(candidate => exported.Contains(candidate));

                var baseName = histogramBase ?? series;

                // Prometheus normalises a counter family by stripping `_total`, so documentation may
                // legitimately name the family (`aveline_process_cpu_seconds`) while the series and
                // the catalog entry carry `_total`.
                var known = exported.Contains(baseName) || exported.Contains($"{baseName}_total");

                known.Should().BeTrue(
                    $"documentation references '{series}', which is neither an "
                    + "ExportedMetric.PrometheusName nor the Prometheus-normalised counter family of one");
            }
        }
    }

    [Fact]
    public void EveryGithubIssueReferenceInTheObservabilityDocumentIsASliceIssue()
    {
        var doc = Read("docs", "backend", "observability.md");

        // The slices were filed as #315..#323; a doc that cites a different range is stale.
        foreach (var expected in Enumerable.Range(315, 9))
        {
            doc.Should().Contain($"issues/{expected})");
        }
    }
}
