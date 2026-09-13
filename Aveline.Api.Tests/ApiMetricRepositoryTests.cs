using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #222 — PostgreSQL proof that the incremental rollup upsert adds to the existing
/// dimension row and preserves the cumulative bucket element sums (BR-6.10, S-24/S-26).
/// </summary>
public class ApiMetricRepositoryTests : IAsyncLifetime
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

    private static readonly DateTime Window =
        new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static ApiRequestMetric Metric(
        Guid? organizationId, long requestCount, int maxDurationMs, params int[] buckets)
        => new()
        {
            OrganizationId = organizationId,
            RouteTemplate = "/api/v1/things/{id}",
            HttpMethod = "GET",
            StatusCode = 200,
            StatusClass = "2xx",
            WindowStart = Window,
            WindowSize = "hour",
            RequestCount = requestCount,
            ErrorCount = 0,
            TotalDurationMs = requestCount * 10,
            MaxDurationMs = maxDurationMs,
            BucketCounts = buckets,
        };

    private async Task<ApiRequestMetric> UpsertAndRead(ApiRequestMetric metric)
    {
        await using var writer = new AppDbContext(_options);
        var repository = new ApiMetricRepository(writer);
        await repository.UpsertAsync([metric]);

        await using var reader = new AppDbContext(_options);
        return await reader.ApiRequestMetrics.AsNoTracking().SingleAsync(row =>
            row.OrganizationId == metric.OrganizationId
            && row.RouteTemplate == metric.RouteTemplate
            && row.HttpMethod == metric.HttpMethod
            && row.StatusCode == metric.StatusCode
            && row.WindowStart == metric.WindowStart);
    }

    [Fact]
    public async Task SecondUpsertOfTheSameDimensionIncrementsInsteadOfDuplicating()
    {
        var organizationId = Guid.CreateVersion7();
        var buckets = new int[12];
        Array.Fill(buckets, 2);

        var first = await UpsertAndRead(Metric(organizationId, 2, 8, buckets));
        Assert.Equal(2, first.RequestCount);
        Assert.Equal(20, first.TotalDurationMs);
        Assert.Equal(8, first.MaxDurationMs);
        Assert.All(first.BucketCounts, count => Assert.Equal(2, count));

        var secondBuckets = new int[12];
        Array.Fill(secondBuckets, 3);
        var second = await UpsertAndRead(Metric(organizationId, 3, 30, secondBuckets));

        Assert.Equal(5, second.RequestCount);
        Assert.Equal(50, second.TotalDurationMs);
        Assert.Equal(30, second.MaxDurationMs);
        Assert.All(second.BucketCounts, count => Assert.Equal(5, count));

        await using var context = new AppDbContext(_options);
        Assert.Equal(1, await context.ApiRequestMetrics.CountAsync());
    }

    [Fact]
    public async Task UpsertWithNullDimensionsStillCollidesOnOneRow()
    {
        var buckets = new int[12];
        Array.Fill(buckets, 1);

        await UpsertAndRead(Metric(null, 1, 5, buckets));
        var merged = await UpsertAndRead(Metric(null, 1, 5, buckets));

        Assert.Equal(2, merged.RequestCount);

        await using var context = new AppDbContext(_options);
        Assert.Equal(1, await context.ApiRequestMetrics.CountAsync());
    }

    [Fact]
    public async Task QueryAlwaysFiltersByOrganization()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var buckets = new int[12];
        Array.Fill(buckets, 1);

        await UpsertAndRead(Metric(first, 1, 5, buckets));
        await UpsertAndRead(Metric(second, 1, 5, buckets));

        await using var context = new AppDbContext(_options);
        var repository = new ApiMetricRepository(context);

        var scoped = await repository.QueryAsync(new ApiStatisticsFilter(first, Window.AddDays(-1), Window.AddDays(1)));
        Assert.Single(scoped);
        Assert.Equal(first, scoped[0].OrganizationId);

        var systemWide = await repository.QueryAsync(new ApiStatisticsFilter(null, Window.AddDays(-1), Window.AddDays(1)));
        Assert.Equal(2, systemWide.Count);
    }
}
