using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Services;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #222 — each S-24…S-32 dimension, explicit organisation scoping, and the
/// <c>null</c>-percentile-below-the-floor behaviour.
/// </summary>
public class ApiStatisticsQueryTests
{
    private static readonly DateTime Window = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(IApiStatisticsService Service, AppDbContext Context, string DatabaseName);

    private static Harness Build(int minSamples = 20)
    {
        var databaseName = $"ApiStatsQuery_{Guid.NewGuid()}";
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

        var options = Options.Create(new TelemetryOptions
        {
            MinSampleForPercentile = minSamples,
            SlowRequestMs = 1000,
        });

        var service = new ApiStatisticsService(
            new ApiMetricRepository(context), new ApiRequestLogRepository(context), context, options);

        return new Harness(service, context, databaseName);
    }

    private static ApiRequestMetric Metric(
        Guid? organizationId,
        long requestCount,
        short statusCode = 200,
        string route = "/api/v1/things/{id}",
        string method = "GET",
        Guid? userId = null,
        Guid? apiKeyId = null,
        int durationMs = 10,
        DateTime? windowStart = null,
        bool throttled = false)
    {
        var buckets = new int[LatencyBuckets.Count];
        for (var i = 0; i < requestCount; i++)
        {
            LatencyBuckets.Add(buckets, durationMs);
        }

        return new ApiRequestMetric
        {
            OrganizationId = organizationId,
            ApiKeyId = apiKeyId,
            UserId = userId,
            RouteTemplate = route,
            HttpMethod = method,
            StatusCode = statusCode,
            StatusClass = $"{statusCode / 100}xx",
            IsThrottled = throttled,
            WindowStart = windowStart ?? Window,
            WindowSize = "hour",
            RequestCount = requestCount,
            ErrorCount = statusCode >= 400 ? requestCount : 0,
            TotalDurationMs = requestCount * durationMs,
            MaxDurationMs = durationMs,
            BucketCounts = buckets,
        };
    }

    private static ApiStatisticsFilter Filter(Guid? organizationId, string? groupBy = null) =>
        new(organizationId, Window.AddDays(-1), Window.AddDays(1), GroupBy: groupBy);

    [Fact]
    public async Task RequestsAggregatesCountsByStatusFamily()
    {
        var harness = Build();
        var org = Guid.CreateVersion7();
        harness.Context.ApiRequestMetrics.AddRange(
            Metric(org, 10, 200),
            Metric(org, 3, 400),
            Metric(org, 2, 500),
            Metric(org, 1, 429, throttled: true));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetRequestsAsync(Filter(org));

        Assert.Equal(16, result.RequestCount);
        Assert.Equal(10, result.SuccessCount);
        Assert.Equal(6, result.ErrorCount);
        Assert.Equal(3, result.ClientErrorCount);
        Assert.Equal(2, result.ServerErrorCount);
        Assert.Equal(1, result.ThrottledCount);
    }

    [Fact]
    public async Task ErrorsSeparatesThrottlingFromClientAndServerErrors()
    {
        var harness = Build();
        var org = Guid.CreateVersion7();
        harness.Context.ApiRequestMetrics.AddRange(
            Metric(org, 10, 200),
            Metric(org, 4, 404),
            Metric(org, 2, 500),
            Metric(org, 2, 429, throttled: true));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetErrorsAsync(Filter(org));

        Assert.Equal(18, result.RequestCount);
        Assert.Equal(4d / 18, result.ClientErrorRate);
        Assert.Equal(2d / 18, result.ServerErrorRate);
        Assert.Equal(2d / 18, result.ThrottleRate);
        Assert.Contains(result.ByStatusCode, item => item.StatusCode == 404 && item.RequestCount == 4);
        Assert.Contains(result.ByRoute, item => item.RouteTemplate == "/api/v1/things/{id}");
    }

    [Fact]
    public async Task LatencyReturnsNullPercentilesBelowTheFloor()
    {
        var harness = Build(minSamples: 20);
        var org = Guid.CreateVersion7();
        harness.Context.ApiRequestMetrics.Add(Metric(org, 5, durationMs: 7));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetLatencyAsync(Filter(org));

        Assert.Null(result.P50Ms);
        Assert.Null(result.P95Ms);
        Assert.Null(result.P99Ms);
        Assert.Equal(LatencyBuckets.InsufficientSamplesReason, result.Reason);
        Assert.Equal(7d, result.AvgMs);
        Assert.Equal("bucket-interpolated", result.Precision);
    }

    [Fact]
    public async Task LatencyInterpolatesAtOrAboveTheFloor()
    {
        var harness = Build(minSamples: 20);
        var org = Guid.CreateVersion7();
        harness.Context.ApiRequestMetrics.Add(Metric(org, 20, durationMs: 5));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetLatencyAsync(Filter(org));

        Assert.Equal(2.5, result.P50Ms);
        Assert.Null(result.Reason);
        Assert.Single(result.Series);
    }

    [Fact]
    public async Task EndpointUsageGroupsByRouteAndMethod()
    {
        var harness = Build();
        var org = Guid.CreateVersion7();
        harness.Context.ApiRequestMetrics.AddRange(
            Metric(org, 3, route: "/api/v1/a", method: "GET"),
            Metric(org, 2, route: "/api/v1/a", method: "GET"),
            Metric(org, 1, route: "/api/v1/a", method: "POST"));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetEndpointsAsync(Filter(org));

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("/api/v1/a", result.Items[0].RouteTemplate);
        Assert.Equal("GET", result.Items[0].HttpMethod);
        Assert.Equal(5, result.Items[0].RequestCount);
    }

    [Fact]
    public async Task UserAndKeyUsageAreOrgScopedAndPaged()
    {
        var harness = Build();
        var org = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var apiKeyId = Guid.CreateVersion7();

        harness.Context.ApiRequestMetrics.AddRange(
            Metric(org, 7, userId: userId, apiKeyId: apiKeyId),
            Metric(org, 2, userId: Guid.CreateVersion7()),
            Metric(other, 100, userId: userId, apiKeyId: apiKeyId));
        harness.Context.ApiKeys.Add(new ApiKey
        {
            Id = apiKeyId,
            OrganizationId = org,
            Name = "Primary",
            Prefix = "avl_live_test",
            KeyHash = new string('a', 64),
            LastUsedAt = Window.AddMinutes(5),
        });
        await harness.Context.SaveChangesAsync();

        var users = await harness.Service.GetUsersAsync(Filter(org));
        Assert.Equal(2, users.Page.Total);

        var keys = await harness.Service.GetApiKeysAsync(Filter(org));
        Assert.Single(keys.Page.Items);
        Assert.Equal(7, keys.Page.Items[0].RequestCount);
        Assert.Equal(Window.AddMinutes(5), keys.Page.Items[0].LastUsedAt);
        Assert.Equal("/api/v1/things/{id}", keys.Page.Items[0].TopEndpoint);
    }

    [Fact]
    public async Task QuotaStatusReadsTheDurablePeriodRows()
    {
        var harness = Build();
        var org = Guid.CreateVersion7();
        harness.Context.ApiQuotaUsage.Add(new ApiQuotaUsage
        {
            OrganizationId = org,
            MetricKey = "api.requests.monthly",
            PeriodStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            LimitValue = 1000,
            UsedValue = 250,
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetQuotaAsync(org);

        Assert.Single(result.Items);
        Assert.Equal(1000, result.Items[0].Limit);
        Assert.Equal(250, result.Items[0].Used);
        Assert.Equal(750, result.Items[0].Remaining);
    }

    [Fact]
    public async Task SlowRequestsComeFromTheRawLog()
    {
        var harness = Build();
        var org = Guid.CreateVersion7();
        harness.Context.ApiRequestLogs.AddRange(
            new ApiRequestLog
            {
                OccurredAt = Window, OrganizationId = org, RouteTemplate = "/api/v1/slow",
                HttpMethod = "GET", StatusCode = 200, DurationMs = 2500, RequestId = "slow-1",
            },
            new ApiRequestLog
            {
                OccurredAt = Window.AddSeconds(1), OrganizationId = org, RouteTemplate = "/api/v1/fast",
                HttpMethod = "GET", StatusCode = 200, DurationMs = 5, RequestId = "fast-1",
            });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetSlowRequestsAsync(Filter(org));

        Assert.Single(result.Page.Items);
        Assert.Equal("slow-1", result.Page.Items[0].RequestId);
        Assert.Equal(2500, result.Page.Items[0].DurationMs);
    }

    [Fact]
    public async Task BillableExcludesHealthOpenApiOptionsAnd499()
    {
        var harness = Build();
        var org = Guid.CreateVersion7();
        harness.Context.ApiRequestMetrics.AddRange(
            Metric(org, 5, route: "/api/v1/things/{id}"),
            Metric(org, 2, route: "/health"),
            Metric(org, 3, route: "/openapi/v1.json"),
            Metric(org, 4, route: "/api/v1/things/{id}", method: "OPTIONS"),
            Metric(org, 1, route: "/api/v1/things/{id}", statusCode: 499));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetBillableAsync(Filter(org));

        Assert.Equal(5, result.BillableRequestCount);
        Assert.Equal(10, result.ExcludedRequestCount);
    }

    [Fact]
    public void WindowValidationRejectsNonUtcReversedAndOverlongWindows()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(ApiStatisticsValidation.TryCreateWindow(
            DateTime.SpecifyKind(now.AddDays(-1), DateTimeKind.Unspecified), now, 92, now, out _, out _, out var nonUtc));
        Assert.NotNull(nonUtc);

        Assert.False(ApiStatisticsValidation.TryCreateWindow(
            now, now.AddDays(-1), 92, now, out _, out _, out var reversed));
        Assert.NotNull(reversed);

        Assert.False(ApiStatisticsValidation.TryCreateWindow(
            now.AddDays(-100), now, 92, now, out _, out _, out var tooLong));
        Assert.NotNull(tooLong);

        Assert.True(ApiStatisticsValidation.TryCreateWindow(
            now.AddDays(-10), now, 92, now, out var start, out var end, out var error));
        Assert.Null(error);
        Assert.Equal(now.AddDays(-10), start);
        Assert.Equal(now, end);
    }
}
