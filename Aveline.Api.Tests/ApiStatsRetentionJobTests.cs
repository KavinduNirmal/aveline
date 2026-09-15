using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #225 — retention: raw logs 7 days, hourly rollups 90 days, daily rollups 400 days
/// (FR-6.4). Newer rows survive.
/// </summary>
public class ApiStatsRetentionJobTests
{
    private static (ApiStatsRetentionJob Job, string DatabaseName) Build()
    {
        var databaseName = $"Retention_{Guid.NewGuid()}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:RawLogRetentionDays"] = "7",
                ["Telemetry:HourlyRollupRetentionDays"] = "90",
                ["Telemetry:DailyRollupRetentionDays"] = "400",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();

        var job = new ApiStatsRetentionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock(),
            NullLogger<ApiStatsRetentionJob>.Instance);

        return (job, databaseName);
    }

    private static AppDbContext Context(string databaseName) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static ApiRequestMetric Metric(DateTime windowStart, string windowSize) => new()
    {
        OrganizationId = Guid.CreateVersion7(),
        RouteTemplate = "/api/v1/things/{id}",
        HttpMethod = "GET",
        StatusCode = 200,
        StatusClass = "2xx",
        WindowStart = windowStart,
        WindowSize = windowSize,
        RequestCount = 1,
        BucketCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
    };

    [Fact]
    public async Task RemovesOnlyRowsPastEachRetentionWindow()
    {
        var (job, databaseName) = Build();
        var now = DateTime.UtcNow;

        await using (var context = Context(databaseName))
        {
            context.ApiRequestMetrics.AddRange(
                Metric(now.AddDays(-200), "hour"),
                Metric(now.AddDays(-500), "day"),
                Metric(now.AddHours(-1), "hour"),
                Metric(now.AddDays(-10), "day"));

            context.ApiRequestLogs.AddRange(
                new ApiRequestLog
                {
                    OccurredAt = now.AddDays(-30), OrganizationId = Guid.CreateVersion7(),
                    RouteTemplate = "/api/v1/old", HttpMethod = "GET", StatusCode = 200,
                },
                new ApiRequestLog
                {
                    OccurredAt = now.AddHours(-1), OrganizationId = Guid.CreateVersion7(),
                    RouteTemplate = "/api/v1/new", HttpMethod = "GET", StatusCode = 200,
                });

            await context.SaveChangesAsync();
        }

        Assert.Equal(3, await job.RunAsync(CancellationToken.None));

        await using var verify = Context(databaseName);
        Assert.Equal(2, await verify.ApiRequestMetrics.CountAsync());
        Assert.Equal(1, await verify.ApiRequestLogs.CountAsync());
        Assert.Contains(
            await verify.ApiRequestMetrics.ToListAsync(),
            metric => metric.WindowSize == "hour" && metric.WindowStart > now.AddDays(-2));
        Assert.Contains(
            await verify.ApiRequestMetrics.ToListAsync(),
            metric => metric.WindowSize == "day" && metric.WindowStart > now.AddDays(-11));
    }
}
