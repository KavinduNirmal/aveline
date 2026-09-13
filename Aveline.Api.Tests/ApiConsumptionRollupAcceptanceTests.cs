using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #226 — the Phase 5 data-quality acceptance test: a synthetic run of exactly 1 000
/// requests produces rollup totals of exactly 1 000. Rollups are never sampled, so the raw
/// log's sample rate cannot shrink them.
/// </summary>
public class ApiConsumptionRollupAcceptanceTests
{
    [Fact]
    public async Task OneThousandRequestsProduceExactlyOneThousandRolledUpRequests()
    {
        const int requestCount = 1000;

        var databaseName = $"Acceptance_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApiMetricRepository, ApiMetricRepository>();
        services.AddScoped<IApiRequestLogRepository, ApiRequestLogRepository>();
        var provider = services.BuildServiceProvider();

        var channel = new TelemetryChannel(requestCount);
        var writer = new ApiTelemetryWriter(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TelemetryOptions { WriterBatchSize = 500 }),
            NullLogger<ApiTelemetryWriter>.Instance);

        var organizationId = Guid.CreateVersion7();
        var window = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < requestCount; i++)
        {
            channel.TryEnqueue(new ApiRequestSample
            {
                OccurredAt = window.AddMilliseconds(i),
                OrganizationId = organizationId,
                RouteTemplate = i % 4 == 0 ? "/api/v1/orders" : "/api/v1/things/{id}",
                HttpMethod = "GET",
                StatusCode = i % 10 == 0 ? (short)500 : (short)200,
                DurationMs = i % 250,
                // Raw persistence is irrelevant to the rollup; keep it off so the sampled log
                // cannot influence the assertion.
                ShouldPersistRaw = false,
            });
        }

        // 1000 samples at 500 per batch needs two drains.
        Assert.Equal(500, await writer.DrainOnceAsync());
        Assert.Equal(500, await writer.DrainOnceAsync());

        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);

        Assert.Equal(requestCount, await context.ApiRequestMetrics.SumAsync(m => m.RequestCount));
        Assert.Equal(
            requestCount,
            await context.ApiRequestMetrics.SumAsync(m => m.BucketCounts[ApiRequestMetricBucketIndex]));
        Assert.Empty(await context.ApiRequestLogs.ToListAsync());

        provider.Dispose();
    }

    // 12th (last) cumulative bucket == RequestCount.
    private const int ApiRequestMetricBucketIndex = 11;
}
