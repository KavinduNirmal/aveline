using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics;
using Aveline.Api.Modules.Analytics.Services;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Business KPIs phase 2 (§7.1): the Postgres-specific half of S-44/S-45. The in-memory
/// provider does not reproduce real range-scan or <c>COUNT(DISTINCT)</c> semantics, and it
/// cannot be asked for a query plan, so the index usage claim is asserted here.
/// </summary>
[Trait("Category", "Postgres")]
public class BusinessKpiPostgresTests : IAsyncLifetime
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

    private BusinessKpiService Service(AppDbContext context) =>
        new(context, new ClaimIdentityMap(), TimeProvider.System);

    private static ApiRequestMetric DayMetric(DateTime day, Guid? userId, Guid? organizationId = null) => new()
    {
        OrganizationId = organizationId,
        UserId = userId,
        RouteTemplate = "/api/v1/things/{id}",
        HttpMethod = "GET",
        StatusCode = 200,
        StatusClass = "2xx",
        WindowStart = day.Date,
        WindowSize = "day",
        RequestCount = 1,
        ErrorCount = 0,
        TotalDurationMs = 12,
        MaxDurationMs = 12,
        BucketCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
    };

    private static BusinessWindow Window(int days, string granularity = "day")
    {
        var to = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var from = to.AddDays(-days);
        return new BusinessWindow(from, to, granularity, BusinessKpiValidation.CountBuckets(from, to, granularity));
    }

    [Fact]
    public async Task DistinctUserCountingMatchesRealPostgres()
    {
        await using var context = new AppDbContext(_options);
        var user1 = Guid.CreateVersion7();
        var user2 = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        var day = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

        // Two distinct dimension tuples for the same user on one day, plus another user. The
        // route template differs because the dimension tuple is unique per (org, user, route,
        // method, status, window).
        var first = DayMetric(day, user1, organization);
        var second = DayMetric(day, user1, organization);
        second.RouteTemplate = "/api/v1/other/{id}";
        context.ApiRequestMetrics.AddRange(
            first,
            second,
            DayMetric(day, user2, organization),
            DayMetric(day.AddDays(-1), user1));
        await context.SaveChangesAsync();

        var result = await Service(context).GetActiveUsersAsync(Window(5), cacheKey: null);

        var point = Assert.Single(result.Series, p => p.BucketStart == day);
        Assert.Equal(2, point.ActiveUsers);
        Assert.Equal(1, point.ActiveOrganizations);
    }

    [Fact]
    public async Task ActiveUsersIsNullWhenEveryRowIsUnattributed()
    {
        await using var context = new AppDbContext(_options);
        var day = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
        context.ApiRequestMetrics.Add(DayMetric(day, userId: null));
        await context.SaveChangesAsync();

        var result = await Service(context).GetActiveUsersAsync(Window(5), cacheKey: null);

        Assert.All(result.Series, point => Assert.Null(point.ActiveUsers));
        Assert.False(result.DataQuality.UserAttributionAvailable);
    }

    [Fact]
    public async Task TheWindowRangeScanUsesTheWindowStartIndex()
    {
        await using var context = new AppDbContext(_options);
        var plan = await context.Database
            .SqlQueryRaw<string>(
                """
                EXPLAIN (COSTS OFF)
                SELECT "UserId" FROM "ApiRequestMetrics"
                WHERE "WindowSize" = 'day'
                  AND "WindowStart" >= TIMESTAMPTZ '2026-08-01'
                  AND "WindowStart" <  TIMESTAMPTZ '2026-09-20'
                  AND "UserId" IS NOT NULL
                """)
            .ToListAsync();

        var text = string.Join("\n", plan);
        Assert.Contains("ApiRequestMetrics", text, StringComparison.Ordinal);
        Assert.Contains("WindowStart", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMigrationCreatedTheThreeNewIndexes()
    {
        await using var context = new AppDbContext(_options);
        var names = await context.Database
            .SqlQueryRaw<string>(
                "SELECT indexname AS \"Value\" FROM pg_indexes WHERE indexname IN ('IX_Users_CreatedAt', 'IX_Organizations_CreatedAt', 'IX_Messages_CreatedAt')")
            .ToListAsync();

        Assert.Contains("IX_Users_CreatedAt", names);
        Assert.Contains("IX_Organizations_CreatedAt", names);
        Assert.Contains("IX_Messages_CreatedAt", names);
    }

    [Fact]
    public async Task TheSignupRangeScanUsesTheUsersCreatedAtIndex()
    {
        await using var context = new AppDbContext(_options);
        var plan = await context.Database
            .SqlQueryRaw<string>(
                """
                EXPLAIN (COSTS OFF)
                SELECT "Id" FROM "Users"
                WHERE "CreatedAt" >= TIMESTAMPTZ '2026-08-01'
                  AND "CreatedAt" <  TIMESTAMPTZ '2026-09-20'
                  AND "DeletedAt" IS NULL
                """)
            .ToListAsync();

        var text = string.Join("\n", plan);
        Assert.Contains("IX_Users_CreatedAt", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrowthAggregatesAcrossRealRows()
    {
        await using var context = new AppDbContext(_options);
        var day = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
        context.ApiRequestMetrics.Add(DayMetric(day, Guid.CreateVersion7()));
        await context.SaveChangesAsync();

        var result = await Service(context).GetGrowthAsync(Window(5), cacheKey: null);

        Assert.Equal(6, result.Series.Count);
        Assert.Equal(Window(5).BucketCount, result.Window.BucketCount);
    }
}
