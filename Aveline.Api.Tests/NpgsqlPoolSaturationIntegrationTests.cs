using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 5 acceptance (plan §9 Slice 5): "the pool series is present in a scrape **after
/// traffic**, not that the registration compiles". Npgsql's pool instruments are observable and
/// only report for pools that exist and have been observed, so this drives a real connection.
/// It also proves S-11 live: the pool-name tag carries the explicit name, not the connection string.
/// </summary>
public class NpgsqlPoolSaturationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private NpgsqlPoolMetricsListener _listener = null!;
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Ordering mirrors production: the listener exists before the data source creates
        // Npgsql's instruments.
        _listener = new NpgsqlPoolMetricsListener();
        _dataSource = DatabaseConfiguration.CreateDataSource(_postgres.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        _listener.Dispose();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task SaturationIsReportedAfterRealPoolTraffic()
    {
        // Open two connections at once so the pool reports state="used" > 0.
        await using var first = await _dataSource.OpenConnectionAsync();
        await using var second = await _dataSource.OpenConnectionAsync();

        foreach (var connection in new[] { first, second })
        {
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
        }

        _listener.TryGetSaturation(out var saturation).Should().BeTrue(
            "after real pool traffic the observable instruments must report");
        saturation.Should().BeGreaterThan(0, "two connections are checked out");
        saturation.Should().BeLessThanOrEqualTo(1);
    }

    // NOTE: an "absent before traffic" assertion is deliberately NOT made here. A MeterListener
    // observes a process-global registry, so in a shared test process it also sees pools created by
    // other test classes; the empty case is covered in isolation by
    // NpgsqlPoolMetricsTests.TryGetSaturation_ReportsNothingBeforeAnyPoolExists.

    [Fact]
    public async Task ThePoolNameTagIsTheExplicitNameNotTheConnectionString()
    {
        await using (var connection = await _dataSource.OpenConnectionAsync())
        {
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
        }

        _listener.PoolNames.Should().Contain(
            DatabaseConfiguration.PoolName,
            "the data source was built with an explicit Name, so the pool label is a bounded identifier");

        // Scope the leak check to THIS test's connection string. The listener observes a
        // process-global meter, so in a full-suite run it also sees pools other test classes created
        // with EF's default naming, and those legitimately carry a connection string.
        var connectionString = _postgres.GetConnectionString();
        _listener.PoolNames.Should().NotContain(
            connectionString,
            "S-11: the pool label must not default to the connection string");
        _listener.PoolNames.Should().NotContain(
            name => name.Contains(connectionString, StringComparison.Ordinal),
            "S-11: no observed pool name may embed this data source's connection string");
    }
}
