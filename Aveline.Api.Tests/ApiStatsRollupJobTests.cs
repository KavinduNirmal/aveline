using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #225 — the hourly rollup recomputes-and-replaces the just-closed hour and is
/// idempotent (BR-6.10).
/// </summary>
public class ApiStatsRollupJobTests
{
    private static readonly DateTime HourStart = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static (ApiStatsRollupJob Job, string DatabaseName) Build()
    {
        var databaseName = $"Rollup_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();

        var job = new ApiStatsRollupJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock(),
            NullLogger<ApiStatsRollupJob>.Instance);

        return (job, databaseName);
    }

    private static AppDbContext Context(string databaseName) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static ApiRequestMetric BrokenMetric() => new()
    {
        OrganizationId = Guid.CreateVersion7(),
        RouteTemplate = "/api/v1/things/{id}",
        HttpMethod = "GET",
        StatusCode = 500,
        StatusClass = "9xx", // wrong, repaired from the status code
        IsThrottled = true,  // wrong for a 500, repaired to false
        WindowStart = HourStart,
        WindowSize = "hour",
        RequestCount = 3,
        ErrorCount = 3,
        TotalDurationMs = 30,
        MaxDurationMs = 20,
        // Non-cumulative and disagreeing with RequestCount.
        BucketCounts = [1, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        RequestBytes = 9,
        ResponseBytes = 30,
    };

    [Fact]
    public async Task RecomputeRepairsDerivedFieldsAndNormalisesBuckets()
    {
        var (_, databaseName) = Build();
        await using (var context = Context(databaseName))
        {
            context.ApiRequestMetrics.Add(BrokenMetric());
            await context.SaveChangesAsync();
        }

        await using var context2 = Context(databaseName);
        Assert.Equal(1, await ApiStatsRollupJob.RecomputeHourAsync(context2, HourStart));

        await using var verify = Context(databaseName);
        var row = await verify.ApiRequestMetrics.SingleAsync();
        Assert.Equal("5xx", row.StatusClass);
        Assert.False(row.IsThrottled);
        Assert.Equal(3, row.BucketCounts[^1]);
        for (var i = 1; i < row.BucketCounts.Length; i++)
        {
            Assert.True(row.BucketCounts[i] >= row.BucketCounts[i - 1]);
        }
    }

    [Fact]
    public async Task RecomputeIsIdempotent()
    {
        var (_, databaseName) = Build();
        await using (var context = Context(databaseName))
        {
            context.ApiRequestMetrics.Add(BrokenMetric());
            await context.SaveChangesAsync();
        }

        await using (var context = Context(databaseName))
        {
            await ApiStatsRollupJob.RecomputeHourAsync(context, HourStart);
        }

        await using (var context = Context(databaseName))
        {
            await ApiStatsRollupJob.RecomputeHourAsync(context, HourStart);
        }

        await using var verify = Context(databaseName);
        var row = await verify.ApiRequestMetrics.SingleAsync();
        Assert.Equal(3, row.RequestCount);
        Assert.Equal(30, row.TotalDurationMs);
        Assert.Equal(3, row.BucketCounts[^1]);
    }

    [Fact]
    public async Task RunProcessesTheJustClosedHour()
    {
        var (job, databaseName) = Build();
        var closedHour = new DateTime(
            DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day,
            DateTime.UtcNow.Hour, 0, 0, DateTimeKind.Utc).AddHours(-1);

        await using (var context = Context(databaseName))
        {
            var metric = BrokenMetric();
            metric.WindowStart = closedHour;
            context.ApiRequestMetrics.Add(metric);
            await context.SaveChangesAsync();
        }

        Assert.Equal(1, await job.RunAsync(CancellationToken.None));

        await using var verify = Context(databaseName);
        var row = await verify.ApiRequestMetrics.SingleAsync();
        Assert.Equal(closedHour, row.WindowStart);
        Assert.Equal("5xx", row.StatusClass);
    }

    [Fact]
    public void NextHourlyRunAtAlignsToTheConfiguredMinute()
    {
        var delay = ApiStatsRollupJob.NextHourlyRunAt(
            new DateTime(2026, 9, 11, 10, 30, 0, DateTimeKind.Utc), minuteUtc: 5);

        Assert.Equal(TimeSpan.FromMinutes(35), delay);
    }

    [Fact]
    public async Task RecomputeOfAnEmptyHourIsANoOp()
    {
        var (_, databaseName) = Build();
        await using var context = Context(databaseName);
        Assert.Equal(0, await ApiStatsRollupJob.RecomputeHourAsync(context, HourStart));
    }
}
