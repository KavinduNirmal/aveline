using System.Text.Json;
using System.Text.RegularExpressions;
using Aveline.Api.Configurations;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 3 (plan §8.1): the Grafana provisioning files and dashboards are configuration, but a
/// typo in a panel's <c>expr</c> or a datasource UID that does not match the provisioned one
/// produces an empty panel with no error. These assertions keep every panel bound to a series the
/// application actually exports.
/// </summary>
public class GrafanaProvisioningTests
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

    private static readonly string[] ProvisioningFiles =
    [
        "datasources/prometheus.yml",
        "dashboards/aveline.yml",
        "alerting/contactpoints.yml",
        "alerting/notification-policies.yml",
        "alerting/rules.yml",
    ];

    [Fact]
    public void EveryProvisioningFileExistsAndIsVersionOne()
    {
        foreach (var relative in ProvisioningFiles)
        {
            var path = Path.Combine(RepositoryRoot, "observability/grafana/provisioning", relative.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(path).Should().BeTrue($"{relative} must be provisioned, not clicked in");

            var content = File.ReadAllText(path);
            content.Should().MatchRegex(@"(?m)^apiVersion:\s*1\s*$", $"{relative} must declare apiVersion: 1");
        }
    }

    [Fact]
    public void DatasourceProvisioningDeclaresTheUidEveryDashboardUses()
    {
        var datasource = Read("observability", "grafana", "provisioning", "datasources", "prometheus.yml");

        datasource.Should().Contain("uid: aveline-prometheus");
        datasource.Should().Contain("isDefault: true");
        datasource.Should().Contain("url: http://prometheus:9090");

        foreach (var uid in DashboardDatasourceUids())
        {
            uid.Should().Be("aveline-prometheus", "a panel must reference the provisioned datasource");
        }
    }

    [Fact]
    public void EveryPanelExpressionReferencesASeriesTheApplicationExports()
    {
        var exported = MetricsCatalog.All.Select(metric => metric.PrometheusName).ToHashSet(StringComparer.Ordinal);
        var expressions = DashboardExpressions();

        expressions.Should().NotBeEmpty("the dashboards must actually query something");

        foreach (var expression in expressions)
        {
            foreach (Match match in Regex.Matches(expression, @"\baveline_[a-z0-9_]+\b"))
            {
                var series = match.Value;
                var histogramBase = new[] { "_bucket", "_sum", "_count" }
                    .Where(suffix => series.EndsWith(suffix, StringComparison.Ordinal))
                    .Select(suffix => series[..^suffix.Length])
                    .FirstOrDefault(candidate => exported.Contains(candidate));

                exported.Should().Contain(
                    histogramBase ?? series,
                    $"dashboard expression '{expression}' references a series that is not exported");
            }
        }
    }

    [Fact]
    public void DashboardsAreProvisionedFromTheMountedPathAndAreNotUiEditable()
    {
        var provider = Read("observability", "grafana", "provisioning", "dashboards", "aveline.yml");

        provider.Should().Contain("path: /var/lib/grafana/dashboards");
        provider.Should().Contain("allowUiUpdates: false");
        provider.Should().Contain("type: file");
    }

    [Fact]
    public void GrafanaServiceIsPinnedAndLockedDown()
    {
        var compose = Read("docker-compose.yml");

        compose.Should().Contain("image: grafana/grafana:13.2.2");
        compose.Should().NotContain("grafana/grafana:13.0.0", "13.0.0 was withdrawn after a migration bug");
        // The lock-down flags are `.env`-overridable but must default to the secure value.
        compose.Should().Contain(
            "GF_AUTH_ANONYMOUS_ENABLED: \"${GRAFANA_ANONYMOUS_ENABLED:-false}\"",
            "anonymous access must default off");
        compose.Should().Contain(
            "GF_USERS_ALLOW_SIGN_UP: \"${GRAFANA_ALLOW_SIGN_UP:-false}\"",
            "self-signup must default off");
        compose.Should().Contain(
            "GF_SECURITY_ALLOW_EMBEDDING: \"${GRAFANA_ALLOW_EMBEDDING:-false}\"",
            "embedding must default off");
        compose.Should().Contain("${GRAFANA_ADMIN_PASSWORD:?", "Grafana must never start with a default password");
    }

    [Fact]
    public void OperatorAlertingIsASingleContactPointAndASinglePolicy()
    {
        var contacts = Read("observability", "grafana", "provisioning", "alerting", "contactpoints.yml");
        var policies = Read("observability", "grafana", "provisioning", "alerting", "notification-policies.yml");

        Regex.Matches(contacts, @"^\s*name:\s*aveline-operators", RegexOptions.Multiline).Count.Should().Be(1);
        policies.Should().Contain("receiver: aveline-operators");
    }

    private static IReadOnlyList<string> DashboardDatasourceUids()
    {
        var uids = new List<string>();
        foreach (var document in DashboardDocuments())
        {
            CollectDatasourceUids(document.RootElement, uids);
        }

        return uids;
    }

    /// <summary>
    /// Collects the <c>uid</c> of every <c>datasource</c> reference. The dashboard's own root
    /// <c>uid</c> is deliberately excluded: it identifies the dashboard, not the datasource.
    /// </summary>
    private static void CollectDatasourceUids(JsonElement element, List<string> uids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("datasource")
                        && property.Value.ValueKind == JsonValueKind.Object
                        && property.Value.TryGetProperty("uid", out var uid)
                        && uid.ValueKind == JsonValueKind.String)
                    {
                        uids.Add(uid.GetString()!);
                    }

                    CollectDatasourceUids(property.Value, uids);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectDatasourceUids(item, uids);
                }

                break;
        }
    }

    private static IReadOnlyList<string> DashboardExpressions()
    {
        var expressions = new List<string>();
        foreach (var document in DashboardDocuments())
        {
            CollectStrings(document.RootElement, "expr", expressions);
        }

        return expressions;
    }

    private static IEnumerable<JsonDocument> DashboardDocuments()
    {
        var directory = Path.Combine(RepositoryRoot, "observability", "grafana", "dashboards");
        var files = Directory.GetFiles(directory, "*.json");
        files.Should().NotBeEmpty("the dashboards are the point of the slice");

        foreach (var file in files)
        {
            yield return JsonDocument.Parse(File.ReadAllText(file));
        }
    }

    private static void CollectStrings(JsonElement element, string propertyName, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        values.Add(property.Value.GetString()!);
                    }

                    CollectStrings(property.Value, propertyName, values);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, propertyName, values);
                }

                break;
        }
    }
}
