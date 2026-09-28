using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 6 (OQ-4, S-10/R-22). The exporter's role must hold pg_monitor and CONNECT and nothing
/// else: reusing the application role would expose every tenant table, and locking it down too far
/// makes several collectors silently return nothing. <c>pg_monitor</c> transitively grants
/// pg_read_all_settings, pg_read_all_stats and pg_stat_scan_tables, which is what pg_locks,
/// pg_stat_activity and pg_stat_replication need.
/// </summary>
public class PostgresExporterRoleTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private NpgsqlConnection _connection = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task TheRoleCanReachTheMonitoringViewsButNotAnApplicationTable()
    {
        await ExecuteAsync(
            """
            CREATE TABLE tenant_data (id int PRIMARY KEY, secret text);
            INSERT INTO tenant_data VALUES (1, 'customer');
            CREATE ROLE postgres_exporter LOGIN PASSWORD 'exporter-test-pw';
            GRANT pg_monitor TO postgres_exporter;
            GRANT CONNECT ON DATABASE aveline_test TO postgres_exporter;
            """);

        // pg_monitor membership is what makes pg_locks/pg_stat_activity readable.
        (await ScalarAsync("SELECT pg_has_role('postgres_exporter', 'pg_monitor', 'MEMBER')"))
            .Should().Be(true);

        // ... and it must not imply access to application data.
        (await ScalarAsync("SELECT has_table_privilege('postgres_exporter', 'tenant_data', 'SELECT')"))
            .Should().Be(false);
    }

    [Fact]
    public async Task TheRoleIsNotASuperuserAndCannotCreateDatabasesOrRoles()
    {
        await ExecuteAsync(
            """
            CREATE ROLE postgres_exporter LOGIN PASSWORD 'exporter-test-pw';
            GRANT pg_monitor TO postgres_exporter;
            """);

        (await ScalarAsync(
            "SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls FROM pg_roles WHERE rolname = 'postgres_exporter'"))
            .Should().Be(false);
    }

    [Fact]
    public void TheCommittedRoleScriptGrantsOnlyPgMonitorAndConnect()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "observability", "postgres-exporter", "role.sql"));

        script.Should().Contain("GRANT pg_monitor TO postgres_exporter");
        script.Should().Contain("GRANT CONNECT ON DATABASE");

        // No table, schema or database-creation privilege is granted.
        script.Should().NotMatchRegex(@"(?i)GRANT\s+.*\s+ON\s+(TABLE|ALL TABLES|SCHEMA)");
        script.Should().NotMatchRegex(@"(?i)\bSUPERUSER\b\s*$");
        script.Should().NotMatchRegex(@"(?i)\b(CREATEDB|CREATEROLE)\b");
    }

    [Fact]
    public void TheExporterComposeServiceIsInternalAndDigestPinned()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "docker-compose.yml"));

        compose.Should().MatchRegex(@"postgres-exporter:\s*\n(?:.*\n)*?.*image:\s*prometheuscommunity/postgres-exporter@sha256:[0-9a-f]{64}");
        compose.Should().NotContain("DATA_SOURCE_NAME", "the password must come from a file, not an inline URL");
        compose.Should().Contain("DATA_SOURCE_PASS_FILE: /run/secrets/postgres_exporter_password");

        // Internal only: no published host port (S-2/S-10).
        ServiceBlock(compose, "postgres-exporter").Should().NotContain("ports:");
        ServiceBlock(compose, "postgres-exporter").Should().Contain("expose:");

        // The three flags that are harmful or removed at v0.20.1. The compose comment names them
        // deliberately to warn against them, so strip comments before asserting.
        var code = WithoutComments(compose);
        code.Should().NotContain("--metric-prefix");
        code.Should().NotContain("--auto-discover-databases");
        code.Should().NotContain("--disable-settings-metrics");
    }

    private static string WithoutComments(string yaml)
        => string.Join(
            '\n',
            yaml.Split('\n')
                .Select(line => line.TrimStart())
                .Where(line => !line.StartsWith('#')));

    [Fact]
    public void TheDatabaseDashboardChartsTheHonestReplicationSignal()
    {
        var dashboard = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "observability", "grafana", "dashboards", "aveline-database.json"));

        var expressions = new List<string>();
        using (var document = System.Text.Json.JsonDocument.Parse(dashboard))
        {
            CollectExpressions(document.RootElement, expressions);
        }

        expressions.Should().Contain(expression => expression.Contains("pg_replication_is_replica"));
        expressions.Should().NotContain(
            expression => expression.Contains("pg_replication_lag_seconds"),
            "lag is hardcoded 0 on a primary, so a lag panel would draw a healthy flat line for a "
            + "deployment with no replica at all (R-23)");

        dashboard.Should().Contain("clamp_min");
        dashboard.Should().Contain("pg_locks_count");
        dashboard.Should().Contain("pg_database_size_bytes");
        dashboard.Should().Contain("pg_stat_activity_count");
    }

    private static void CollectExpressions(System.Text.Json.JsonElement element, List<string> expressions)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("expr") && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        expressions.Add(property.Value.GetString()!);
                    }

                    CollectExpressions(property.Value, expressions);
                }

                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectExpressions(item, expressions);
                }

                break;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    /// <summary>Slices one compose service's own block out, so a sibling's keys cannot satisfy an assertion.</summary>
    private static string ServiceBlock(string compose, string service)
    {
        var start = compose.IndexOf($"  {service}:", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{service} must be a compose service");

        var bodyStart = compose.IndexOf('\n', start) + 1;
        var next = System.Text.RegularExpressions.Regex.Match(
            compose[bodyStart..], @"^  \S", System.Text.RegularExpressions.RegexOptions.Multiline);

        return next.Success ? compose[bodyStart..(bodyStart + next.Index)] : compose[bodyStart..];
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> ScalarAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
