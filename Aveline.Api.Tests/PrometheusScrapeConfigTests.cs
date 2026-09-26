using System.Text.RegularExpressions;
using Aveline.Api.Configurations;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 1 (plan §8.1, §8.3): the committed Prometheus configuration parses, every scrape job has
/// a target and an existing credential-file reference, the retention setting lives in the config
/// file rather than the deprecated flag, and every <c>aveline_</c> series a rule references is one
/// the application actually exports. <c>promtool</c> remains the authoritative validator; this is
/// the fast, dependency-free layer that fails before a container is pulled.
/// </summary>
public class PrometheusScrapeConfigTests
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

            directory.Should().NotBeNull("the test must run from inside the repository");
            return directory!.FullName;
        }
    }

    private static string Read(params string[] relativeParts)
        => File.ReadAllText(Path.Combine(RepositoryRoot, Path.Combine(relativeParts)));

    private static string PrometheusYaml => Read("observability", "prometheus", "prometheus.yml");

    private static string RulesYaml => Read("observability", "prometheus", "rules", "aveline.yml");

    [Fact]
    public void PrometheusConfigDeclaresEveryScrapeJob()
    {
        var jobs = Regex.Matches(PrometheusYaml, @"^\s*-\s*job_name:\s*(?<name>\S+)\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups["name"].Value)
            .ToArray();

        jobs.Should().Contain(new[] { "aveline-api", "aveline-postgres", "prometheus" });
    }

    [Fact]
    public void EveryScrapeJobHasATarget()
    {
        // A job block ends at the next job_name (or end of file); every block must carry targets.
        var blocks = Regex.Split(PrometheusYaml, @"^\s*-\s*job_name:", RegexOptions.Multiline).Skip(1);

        foreach (var block in blocks)
        {
            block.Should().MatchRegex(@"targets:\s*\[.+\]", "every scrape job needs at least one target");
        }
    }

    [Fact]
    public void ApiScrapeJobUsesABearerCredentialFile()
    {
        PrometheusYaml.Should().Contain("credentials_file: /etc/prometheus/secrets/scrape_token");
        PrometheusYaml.Should().Contain("type: Bearer");

        // check config stats referenced files, so the CI job and the operator must create it.
        File.Exists(Path.Combine(RepositoryRoot, "observability", "prometheus", "secrets", ".gitkeep"))
            .Should().BeTrue("the secrets directory must be tracked so promtool has a mount point");
    }

    [Fact]
    public void RetentionIsInTheConfigFileAndNotTheDeprecatedFlag()
    {
        PrometheusYaml.Should().MatchRegex(@"retention:\s*\r?\n\s*time:\s*15d");

        // Comments in the compose file deliberately name the deprecated flag to warn against it,
        // so strip comments before asserting that no command argument passes it.
        WithoutComments(Read("docker-compose.yml")).Should().NotContain("--storage.tsdb.retention");
    }

    [Fact]
    public void PrometheusAndCollectorImagesArePinned()
    {
        var compose = Read("docker-compose.yml");
        compose.Should().NotContain("prom/prometheus:v3.5.0", "3.5 is end of life");
        compose.Should().MatchRegex(@"image:\s*prom/prometheus:v3\.1[34]\.");
        compose.Should().MatchRegex(@"image:\s*otel/opentelemetry-collector-contrib:0\.161\.0");
    }

    [Fact]
    public void RuleFilesResolveToACommittedRulesFile()
    {
        PrometheusYaml.Should().Contain("rule_files:");

        Directory.Exists(Path.Combine(RepositoryRoot, "observability", "prometheus", "rules"))
            .Should().BeTrue();
        RulesYaml.Should().Contain("groups:");
    }

    [Fact]
    public void EveryAvelineSeriesInTheRulesIsExportedByTheApplication()
    {
        var exported = MetricsCatalog.All.Select(metric => metric.PrometheusName).ToHashSet(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(RulesYaml, @"\baveline_[a-z0-9_]+\b"))
        {
            var series = match.Value;

            // A histogram publishes `<name>_bucket`, `<name>_sum` and `<name>_count`; strip a
            // suffix only when the stripped name is itself a catalog entry, so
            // `aveline_blossom_balance_count` is not mistaken for the `_count` of a histogram.
            var histogramBase = new[] { "_bucket", "_sum", "_count" }
                .Where(suffix => series.EndsWith(suffix, StringComparison.Ordinal))
                .Select(suffix => series[..^suffix.Length])
                .FirstOrDefault(candidate => exported.Contains(candidate));

            var baseName = histogramBase ?? series;

            exported.Should().Contain(
                baseName,
                $"the rule references '{series}', which is not an ExportedMetric.PrometheusName "
                + "(see MetricsCatalog)");
        }
    }

    [Fact]
    public void AlertingRulesCarrySeverityAndDuration()
    {
        foreach (Match alert in Regex.Matches(RulesYaml, @"-\s*alert:\s*(?<name>\S+)"))
        {
            var start = alert.Index;
            var end = RulesYaml.IndexOf("- alert:", start + 1, StringComparison.Ordinal);
            var block = end < 0 ? RulesYaml[start..] : RulesYaml[start..end];

            block.Should().MatchRegex(@"expr:\s*\S", $"{alert.Groups["name"].Value} needs an expr");
            block.Should().MatchRegex(@"for:\s*\S", $"{alert.Groups["name"].Value} needs a for duration");
            block.Should().Contain("severity:", $"{alert.Groups["name"].Value} needs a severity label");
        }
    }

    [Fact]
    public void ScrapeTokenHasNoCommittedDefault()
    {
        var env = Read(".env.example");
        env.Should().MatchRegex(@"(?m)^METRICS_SCRAPE_TOKEN=\s*$", "the token must ship empty, never a default");

        var compose = Read("docker-compose.yml");
        compose.Should().Contain("${METRICS_SCRAPE_TOKEN:?", "an unset token must fail the stack");
    }

    [Fact]
    public void CollectorReceivesAMetricsPipelineAndNoDeprecatedForms()
    {
        var collector = WithoutComments(Read("otel-collector-config.yaml"));

        collector.Should().Contain("metrics:");
        collector.Should().Contain("prometheus:");
        collector.Should().Contain("otlp_grpc/jaeger", "the deprecated `otlp` alias logs on every boot");
        collector.Should().Contain("resource_constant_labels", "resource_to_telemetry_conversion is deprecated");
        collector.Should().NotContain("resource_to_telemetry_conversion");
    }

    /// <summary>Drops full-line YAML comments, which name the deprecated forms deliberately.</summary>
    private static string WithoutComments(string yaml)
        => string.Join(
            '\n',
            yaml.Split('\n')
                .Select(line => line.TrimStart())
                .Where(line => !line.StartsWith('#')));

    [Fact]
    public void NoBannedPostgresExporterFlagsArePresent()
    {
        // The compose comment names the banned flags deliberately to warn against them, so strip
        // comments before asserting that no command argument passes one.
        var compose = WithoutComments(Read("docker-compose.yml"));

        compose.Should().NotContain("--metric-prefix");
        compose.Should().NotContain("--auto-discover-databases");
        compose.Should().NotContain("--disable-settings-metrics");
    }
}
