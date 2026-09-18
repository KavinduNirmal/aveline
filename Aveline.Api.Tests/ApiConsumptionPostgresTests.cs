using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #220 — PostgreSQL-backed invariants for the M7 schema: the
/// <c>NULLS NOT DISTINCT</c> dimension upsert target, daily range partitioning of
/// <c>ApiRequestLogs</c>, and the two partition-management SQL functions.
/// </summary>
public class ApiConsumptionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static ApiRequestMetric Metric(Guid? organizationId, string route = "/api/v1/things")
        => new()
        {
            OrganizationId = organizationId,
            RouteTemplate = route,
            HttpMethod = "GET",
            StatusCode = 200,
            StatusClass = "2xx",
            IsThrottled = false,
            WindowStart = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc),
            WindowSize = "hour",
            RequestCount = 1,
            ErrorCount = 0,
            TotalDurationMs = 12,
            MaxDurationMs = 12,
            BucketCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
        };

    [Fact]
    public async Task DuplicateDimensionTupleWithNulls_IsRejected()
    {
        await using var context = new AppDbContext(_options);
        context.ApiRequestMetrics.Add(Metric(null));
        await context.SaveChangesAsync();

        context.ApiRequestMetrics.Add(Metric(null));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task TheDimensionUniqueIndex_IsNullsNotDistinct()
    {
        await using var context = new AppDbContext(_options);
        var definition = await context.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'IX_ApiRequestMetrics_Dimensions'")
            .SingleAsync();

        Assert.Contains("NULLS NOT DISTINCT", definition);
    }

    [Fact]
    public async Task ARequestLogRoutesIntoTheDailyPartition()
    {
        var occurredAt = DateTime.UtcNow;
        await using var context = new AppDbContext(_options);
        context.ApiRequestLogs.Add(new ApiRequestLog
        {
            OccurredAt = occurredAt,
            RouteTemplate = "/api/v1/things/{id}",
            HttpMethod = "GET",
            StatusCode = 200,
            DurationMs = 5,
            RequestId = "partition-probe",
        });
        await context.SaveChangesAsync();

        var table = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.relname AS "Value"
                FROM "ApiRequestLogs" l
                JOIN pg_class c ON c.oid = l.tableoid
                WHERE l."RequestId" = 'partition-probe'
                """)
            .SingleAsync();

        Assert.Equal($"ApiRequestLogs_{occurredAt:yyyyMMdd}", table);

        var inTree = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.relname AS "Value"
                FROM pg_partition_tree('"ApiRequestLogs"'::regclass) t
                JOIN pg_class c ON c.oid = t.relid
                WHERE t.isleaf
                """)
            .ToListAsync();

        Assert.Contains($"ApiRequestLogs_{occurredAt:yyyyMMdd}", inTree);
    }

    [Fact]
    public async Task ApiRequestLogs_IsDeclarativelyRangePartitionedByDay()
    {
        await using var context = new AppDbContext(_options);
        var partitionKey = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT pg_get_partkeydef(c.oid) AS "Value"
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relname = 'ApiRequestLogs' AND c.relkind = 'p'
                """)
            .SingleAsync();

        Assert.Contains("RANGE", partitionKey);
        Assert.Contains("OccurredAt", partitionKey);
    }

    [Fact]
    public async Task EnsurePartitionFunction_CreatesTheRequestedDay()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT aveline_ensure_api_request_log_partition('{day:yyyy-MM-dd}'::date)");

        var exists = await context.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pg_class WHERE relname = {0}",
                $"ApiRequestLogs_{day:yyyyMMdd}")
            .SingleAsync();

        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task DropOldPartitionsFunction_DropsPartitionsPastRetention()
    {
        var oldDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var partitionName = $"ApiRequestLogs_{oldDay:yyyyMMdd}";

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT aveline_ensure_api_request_log_partition('{oldDay:yyyy-MM-dd}'::date)");

        await context.Database.ExecuteSqlRawAsync(
            "SELECT aveline_drop_old_api_request_log_partitions(7)");

        var exists = await context.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pg_class WHERE relname = {0}",
                partitionName)
            .SingleAsync();

        Assert.Equal(0, exists);
    }

    [Fact]
    public async Task RawLogPartialIndexes_Exist()
    {
        await using var context = new AppDbContext(_options);
        var names = await context.Database
            .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'ApiRequestLogs'")
            .ToListAsync();

        Assert.Contains("IX_ApiRequestLogs_Org_Occurred", names);
        Assert.Contains("IX_ApiRequestLogs_Key_Occurred", names);
        Assert.Contains("IX_ApiRequestLogs_Slow", names);
        Assert.Contains("IX_ApiRequestLogs_Errors", names);
        Assert.Contains("IX_ApiRequestLogs_RequestId", names);
    }
}
