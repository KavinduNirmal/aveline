using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #228 — the daily system metric retention job. The cutoff follows
/// <c>Observability:SystemMetricRetentionDays</c> (default 30) and a re-run is a no-op.
/// </summary>
public class SystemMetricRetentionJobTests
{
    private static (SystemMetricRetentionJob Job, string DatabaseName) Build(int retentionDays = 30)
    {
        var databaseName = $"SystemMetricRetention_{Guid.NewGuid()}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:SystemMetricRetentionDays"] = retentionDays.ToString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ISystemMetricRepository, SystemMetricRepository>();
        var provider = services.BuildServiceProvider();

        var job = new SystemMetricRetentionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock(),
            NullLogger<SystemMetricRetentionJob>.Instance);

        return (job, databaseName);
    }

    private static AppDbContext Context(string databaseName) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static SystemMetricSample Sample(DateTime sampledAt) => new()
    {
        MetricName = "aveline.process.thread_count",
        DimensionsJson = "{}",
        DimensionHash = new string('a', 64),
        ValueBigint = 4,
        Unit = "count",
        WindowStart = sampledAt,
        WindowSize = "instant",
        SampledAt = sampledAt,
    };

    [Fact]
    public async Task DeletesOnlySamplesOlderThanTheConfiguredCutoff()
    {
        var (job, databaseName) = Build();
        var now = DateTime.UtcNow;

        await using (var context = Context(databaseName))
        {
            context.SystemMetricSamples.AddRange(
                Sample(now.AddDays(-40)),
                Sample(now.AddDays(-1)));
            await context.SaveChangesAsync();
        }

        Assert.Equal(1, await job.RunAsync(CancellationToken.None));

        await using var verify = Context(databaseName);
        var remaining = Assert.Single(await verify.SystemMetricSamples.ToListAsync());
        Assert.True(remaining.SampledAt > now.AddDays(-2));
    }

    [Fact]
    public async Task ReRunIsANoOp()
    {
        var (job, databaseName) = Build();
        var now = DateTime.UtcNow;

        await using (var context = Context(databaseName))
        {
            context.SystemMetricSamples.Add(Sample(now.AddDays(-45)));
            await context.SaveChangesAsync();
        }

        Assert.Equal(1, await job.RunAsync(CancellationToken.None));
        Assert.Equal(0, await job.RunAsync(CancellationToken.None));

        await using var verify = Context(databaseName);
        Assert.Equal(0, await verify.SystemMetricSamples.CountAsync());
    }
}
