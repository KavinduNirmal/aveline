using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #221 — the telemetry writer folds a batch into the rollup (never sampled) and
/// writes only the sampled subset to the raw log.
/// </summary>
public class ApiTelemetryWriterTests
{
    private sealed class Harness
    {
        public required ApiTelemetryWriter Writer { get; init; }
        public required TelemetryChannel Channel { get; init; }
        public required string DatabaseName { get; init; }
        public required ServiceProvider Provider { get; init; }

        public AppDbContext Context() => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(DatabaseName)
                .Options);
    }

    private static Harness Build(int capacity = 100, int batchSize = 500)
    {
        var databaseName = $"TelemetryWriter_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApiMetricRepository, ApiMetricRepository>();
        services.AddScoped<IApiRequestLogRepository, ApiRequestLogRepository>();
        var provider = services.BuildServiceProvider();

        var channel = new TelemetryChannel(capacity);
        var options = new TelemetryOptions
        {
            BufferCapacity = capacity,
            WriterBatchSize = batchSize,
            WriterFlushSeconds = 1,
        };

        return new Harness
        {
            Channel = channel,
            DatabaseName = databaseName,
            Provider = provider,
            Writer = new ApiTelemetryWriter(
                channel,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(options),
                NullLogger<ApiTelemetryWriter>.Instance),
        };
    }

    private static ApiRequestSample Sample(
        Guid? organizationId = null, short statusCode = 200, int durationMs = 3,
        bool persistRaw = false, string route = "/api/v1/things/{id}", DateTime? occurredAt = null)
        => new()
        {
            OccurredAt = occurredAt ?? new DateTime(2026, 9, 11, 10, 15, 0, DateTimeKind.Utc),
            OrganizationId = organizationId,
            RouteTemplate = route,
            HttpMethod = "GET",
            StatusCode = statusCode,
            DurationMs = durationMs,
            ShouldPersistRaw = persistRaw,
        };

    [Fact]
    public async Task EmptyChannel_DrainsNothing()
    {
        var harness = Build();
        Assert.Equal(0, await harness.Writer.DrainOnceAsync());
    }

    [Fact]
    public async Task BatchUpsertIncrementsTheRollupRow()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();

        harness.Channel.TryEnqueue(Sample(organizationId, durationMs: 3));
        harness.Channel.TryEnqueue(Sample(organizationId, durationMs: 7));
        Assert.Equal(2, await harness.Writer.DrainOnceAsync());

        await using var context = harness.Context();
        var metric = await context.ApiRequestMetrics.SingleAsync();
        Assert.Equal(organizationId, metric.OrganizationId);
        Assert.Equal(2, metric.RequestCount);
        Assert.Equal(10, metric.TotalDurationMs);
        Assert.Equal(7, metric.MaxDurationMs);
        Assert.Equal(2, metric.BucketCounts[^1]);
        Assert.Equal(new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc), metric.WindowStart);

        // A second batch for the same dimension increments rather than duplicates.
        harness.Channel.TryEnqueue(Sample(organizationId, durationMs: 3));
        Assert.Equal(1, await harness.Writer.DrainOnceAsync());

        await using var second = harness.Context();
        var updated = await second.ApiRequestMetrics.SingleAsync();
        Assert.Equal(3, updated.RequestCount);

        harness.Provider.Dispose();
    }

    [Fact]
    public async Task RollupsIncludeNonSampledRequests_ButRawLogDoesNot()
    {
        var harness = Build();
        var baseTime = new DateTime(2026, 9, 11, 10, 15, 0, DateTimeKind.Utc);

        // successes: 3 sampled out, 3 not; one error always sampled.
        for (var i = 0; i < 6; i++)
        {
            // Distinct timestamps keep the in-memory composite (Id, OccurredAt) key unique;
            // PostgreSQL identity assigns the Id.
            harness.Channel.TryEnqueue(Sample(
                statusCode: 200, persistRaw: i < 3, occurredAt: baseTime.AddSeconds(i)));
        }

        harness.Channel.TryEnqueue(Sample(
            statusCode: 500, persistRaw: true, occurredAt: baseTime.AddSeconds(10)));

        Assert.Equal(7, await harness.Writer.DrainOnceAsync());

        await using var context = harness.Context();
        Assert.Equal(7, await context.ApiRequestMetrics.SumAsync(m => m.RequestCount));
        Assert.Equal(4, await context.ApiRequestLogs.CountAsync());

        harness.Provider.Dispose();
    }

    [Fact]
    public async Task WritesAtMostOneBatchPerDrain()
    {
        var harness = Build(batchSize: 2);
        harness.Channel.TryEnqueue(Sample(route: "/a"));
        harness.Channel.TryEnqueue(Sample(route: "/b"));
        harness.Channel.TryEnqueue(Sample(route: "/c"));

        Assert.Equal(2, await harness.Writer.DrainOnceAsync());

        await using var context = harness.Context();
        Assert.Equal(2, await context.ApiRequestMetrics.CountAsync());

        harness.Provider.Dispose();
    }
}
