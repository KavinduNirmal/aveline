using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #225 — the partition job creates tomorrow's daily partition and drops partitions
/// past the raw retention through the M7 SQL functions.
/// </summary>
public class ApiRequestLogPartitionJobTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using (var context = new AppDbContext(options))
        {
            await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
            await context.Database.MigrateAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:RawLogRetentionDays"] = "7",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(builder => builder.UseNpgsql(
            _postgres.GetConnectionString(),
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task CreatesTomorrowAndDropsPartitionsPastRetention()
    {
        var oldDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var oldPartition = $"ApiRequestLogs_{oldDay:yyyyMMdd}";
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var tomorrowPartition = $"ApiRequestLogs_{tomorrow:yyyyMMdd}";

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT aveline_ensure_api_request_log_partition('{oldDay:yyyy-MM-dd}'::date)");
        }

        var job = new ApiRequestLogPartitionJob(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock(),
            NullLogger<ApiRequestLogPartitionJob>.Instance);

        Assert.True(await job.RunAsync(CancellationToken.None) >= 1);

        using var verifyScope = _provider.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var partitions = await verify.Database
            .SqlQueryRaw<string>("SELECT relname AS \"Value\" FROM pg_class WHERE relkind = 'r'")
            .ToListAsync();

        Assert.Contains(tomorrowPartition, partitions);
        Assert.DoesNotContain(oldPartition, partitions);
    }
}
