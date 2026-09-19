using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics;
using Aveline.Api.Modules.Analytics.DTOs;
using Aveline.Api.Modules.Analytics.Services;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Business KPIs phase 3 (§5.3 S-48/S-49): the five usage measures, the optional
/// organization scope, and the organization ranking.
/// </summary>
public class BusinessUsageQueryTests
{
    private static readonly DateTime To = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Harness(BusinessKpiService Service, AppDbContext Context);

    private static Harness Build()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BusinessUsage_{Guid.NewGuid()}")
            .Options);

        return new Harness(
            new BusinessKpiService(
                context,
                new ClaimIdentityMap(),
                new FixedTimeProvider(new DateTimeOffset(To, TimeSpan.Zero))),
            context);
    }

    private static BusinessWindow Window(int days, string granularity = "day")
    {
        var from = To.AddDays(-days);
        return new BusinessWindow(from, To, granularity, BusinessKpiValidation.CountBuckets(from, To, granularity));
    }

    private static Conversation AConversation(Guid organizationId, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        OrganizationId = organizationId,
        OwnerUserId = Guid.CreateVersion7(),
        Kind = ConversationKind.Salon,
        Status = ConversationStatus.Active,
        CreatedAt = To.AddDays(-10),
        LastMessageAt = To.AddDays(-1),
    };

    private static Message AMessage(Guid conversationId, DateTime createdAt, Guid? authorUserId = null) => new()
    {
        ConversationId = conversationId,
        AuthorKind = AuthorKind.User,
        AuthorUserId = authorUserId ?? Guid.CreateVersion7(),
        Kind = MessageKind.Note,
        Status = MessageStatus.Sent,
        CreatedAt = createdAt,
    };

    private static DailyAgentMetric AnAgentMetric(Guid organizationId, DateTime day, int runs = 1) => new()
    {
        OrganizationId = organizationId,
        AgentKey = "orchestrator",
        Day = day.Date,
        RunCount = runs,
        SucceededCount = runs,
    };

    private static ApiRequestMetric AnApiMetric(Guid? organizationId, DateTime day, long requests = 1) => new()
    {
        OrganizationId = organizationId,
        UserId = Guid.CreateVersion7(),
        RouteTemplate = "/api/v1/things/{id}",
        HttpMethod = "GET",
        StatusCode = 200,
        StatusClass = "2xx",
        WindowStart = day.Date,
        WindowSize = "day",
        RequestCount = requests,
        BucketCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
    };

    private static DailyBillingMetric ABillingMetric(Guid organizationId, DateTime day, decimal units, decimal cost) => new()
    {
        OrganizationId = organizationId,
        Day = day.Date,
        PlanTier = PlanTier.Bloom,
        Provider = "openai",
        Model = "gpt-4o-mini",
        RequestCount = 1,
        BlossomUnits = units,
        ActualCostUsd = cost,
    };

    // ── The four measures ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UsageSumsMessagesSentPerBucket()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        var conversation = AConversation(organizationId);
        var day = To.Date.AddDays(-2);
        harness.Context.Conversations.Add(conversation);
        harness.Context.Messages.AddRange(
            AMessage(conversation.Id, day.AddHours(1)),
            AMessage(conversation.Id, day.AddHours(2)),
            AMessage(conversation.Id, To.Date.AddDays(-3)));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        Assert.Equal(2, Assert.Single(result.Series, point => point.BucketStart == day).MessagesSent);
        Assert.Equal(3, result.Totals.MessagesSent);
    }

    [Fact]
    public async Task UsageSumsAgentRunsFromTheDailyRollup()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        harness.Context.DailyAgentMetrics.AddRange(
            AnAgentMetric(organizationId, To.Date.AddDays(-2), runs: 3),
            AnAgentMetric(organizationId, To.Date.AddDays(-2), runs: 2));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        Assert.Equal(5, Assert.Single(result.Series, point => point.BucketStart == To.Date.AddDays(-2)).AgentRuns);
        Assert.Equal(5, result.Totals.AgentRuns);
    }

    [Fact]
    public async Task UsageSumsApiRequestsIncludingUnattributedOnesForAPlatformTotal()
    {
        var harness = Build();
        harness.Context.ApiRequestMetrics.AddRange(
            AnApiMetric(Guid.CreateVersion7(), To.Date.AddDays(-1), requests: 4),
            // BR-6.1: an unattributed request still happened and counts toward a platform total.
            AnApiMetric(null, To.Date.AddDays(-1), requests: 6));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        Assert.Equal(10, result.Totals.ApiRequests);
    }

    [Fact]
    public async Task UsageSumsBlossomUnitsAndActualCost()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        harness.Context.DailyBillingMetrics.AddRange(
            ABillingMetric(organizationId, To.Date.AddDays(-2), units: 12.5m, cost: 0.25m),
            ABillingMetric(organizationId, To.Date.AddDays(-2), units: 7.5m, cost: 0.10m));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        var point = Assert.Single(result.Series, p => p.BucketStart == To.Date.AddDays(-2));
        Assert.Equal(20m, point.BlossomUnits);
        Assert.Equal(0.35m, point.ActualCostUsd);
    }

    [Fact]
    public async Task UsageScopesEveryMeasureToTheRequestedOrganization()
    {
        var harness = Build();
        var mine = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var mineConversation = AConversation(mine);
        var otherConversation = AConversation(other);
        harness.Context.Conversations.AddRange(mineConversation, otherConversation);
        harness.Context.Messages.AddRange(
            AMessage(mineConversation.Id, To.Date.AddDays(-1)),
            AMessage(otherConversation.Id, To.Date.AddDays(-1)),
            AMessage(otherConversation.Id, To.Date.AddDays(-1)));
        harness.Context.DailyAgentMetrics.AddRange(
            AnAgentMetric(mine, To.Date.AddDays(-1), runs: 2),
            AnAgentMetric(other, To.Date.AddDays(-1), runs: 9));
        harness.Context.ApiRequestMetrics.AddRange(
            AnApiMetric(mine, To.Date.AddDays(-1), requests: 3),
            AnApiMetric(other, To.Date.AddDays(-1), requests: 30),
            AnApiMetric(null, To.Date.AddDays(-1), requests: 300));
        harness.Context.DailyBillingMetrics.AddRange(
            ABillingMetric(mine, To.Date.AddDays(-1), units: 1m, cost: 0.01m),
            ABillingMetric(other, To.Date.AddDays(-1), units: 99m, cost: 0.99m));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetUsageAsync(Window(7), mine, cacheKey: null);

        Assert.Equal(mine, result.OrganizationId);
        Assert.Equal(1, result.Totals.MessagesSent);
        Assert.Equal(2, result.Totals.AgentRuns);
        // Unattributed rows belong to no organization, so a scoped read excludes them.
        Assert.Equal(3, result.Totals.ApiRequests);
        Assert.Equal(1m, result.Totals.BlossomUnits);
    }

    [Fact]
    public async Task UsageReportsTheUnattributedInclusionForAPlatformTotal()
    {
        var harness = Build();
        harness.Context.ApiRequestMetrics.Add(AnApiMetric(null, To.Date.AddDays(-1), requests: 5));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        Assert.Contains(
            result.DataQuality.Notes,
            note => note.Contains("attribution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UsagePropagatesTheAgentUninstrumentedCaveat()
    {
        var harness = Build();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        Assert.True(result.DataQuality.AgentMetricsUninstrumented);
        Assert.Contains(
            result.DataQuality.Notes,
            note => note.Contains("agent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UsageEmitsZeroForAQuietDayInsideTheWindow()
    {
        var harness = Build();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        Assert.All(result.Series, point => Assert.Equal(0, point.MessagesSent));
        Assert.Equal(UsageTotalsDto.Empty, result.Totals);
    }

    [Fact]
    public async Task UsageIgnoresMessagesOutsideTheWindow()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        var conversation = AConversation(organizationId);
        harness.Context.Conversations.Add(conversation);
        harness.Context.Messages.Add(AMessage(conversation.Id, To.AddDays(-30)));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetUsageAsync(Window(7), null, cacheKey: null);

        Assert.Equal(0, result.Totals.MessagesSent);
    }

    // ── S-49 organization ranking ─────────────────────────────────────────────────────────

    private static Organization AnOrganization(Guid id, string name, PlanTier tier = PlanTier.Bloom) => new()
    {
        Id = id,
        Name = name,
        Slug = $"org-{id:N}",
        OwnerUserId = Guid.CreateVersion7(),
        PlanTier = tier,
    };

    [Fact]
    public async Task RankingOrdersByTheRequestedMetric()
    {
        var harness = Build();
        var busy = Guid.CreateVersion7();
        var quiet = Guid.CreateVersion7();
        harness.Context.Organizations.AddRange(AnOrganization(busy, "Busy"), AnOrganization(quiet, "Quiet"));
        harness.Context.DailyBillingMetrics.AddRange(
            ABillingMetric(busy, To.Date.AddDays(-1), units: 100m, cost: 1m),
            ABillingMetric(quiet, To.Date.AddDays(-1), units: 1m, cost: 0.01m));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.BlossomUnits, limit: 10, cacheKey: null);

        Assert.Equal(busy, result.Items[0].OrganizationId);
        Assert.Equal(1, result.Items[0].Rank);
        Assert.Equal(quiet, result.Items[1].OrganizationId);
        Assert.Equal(2, result.Items[1].Rank);
    }

    [Fact]
    public async Task RankingClampsToTheRequestedLimit()
    {
        var harness = Build();
        for (var i = 0; i < 5; i++)
        {
            var id = Guid.CreateVersion7();
            harness.Context.Organizations.Add(AnOrganization(id, $"Org {i}"));
            harness.Context.DailyAgentMetrics.Add(AnAgentMetric(id, To.Date.AddDays(-1), runs: i + 1));
        }

        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.AgentRuns, limit: 2, cacheKey: null);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.Items[0].AgentRuns);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task RankingBreaksTiesDeterministicallyByOrganizationId()
    {
        var harness = Build();
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        foreach (var id in ids)
        {
            harness.Context.Organizations.Add(AnOrganization(id, $"Org {id:N}"));
            harness.Context.DailyAgentMetrics.Add(AnAgentMetric(id, To.Date.AddDays(-1), runs: 4));
        }

        await harness.Context.SaveChangesAsync();

        var first = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.AgentRuns, limit: 10, cacheKey: null);
        var second = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.AgentRuns, limit: 10, cacheKey: null);

        Assert.Equal(
            first.Items.Select(item => item.OrganizationId),
            second.Items.Select(item => item.OrganizationId));
        Assert.Equal(ids.OrderBy(id => id).ToArray(), first.Items.Select(item => item.OrganizationId).ToArray());
    }

    [Fact]
    public async Task RankingReportsLastActivityFromTheGreatestOfTheReconstructedSources()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        harness.Context.Organizations.Add(AnOrganization(organizationId, "Atelier"));
        var conversation = AConversation(organizationId);
        conversation.LastMessageAt = To.AddDays(-3);
        harness.Context.Conversations.Add(conversation);
        harness.Context.DailyAgentMetrics.Add(AnAgentMetric(organizationId, To.Date.AddDays(-1)));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.AgentRuns, limit: 10, cacheKey: null);

        var item = Assert.Single(result.Items);
        Assert.Equal(To.Date.AddDays(-1), item.LastActivityAt);
        Assert.Equal(1, item.DaysSinceLastActivity);
        Assert.True(result.DataQuality.LastActivityIsReconstructed);
    }

    [Fact]
    public async Task RankingLeavesDaysSinceLastActivityNullWhenNothingIsRecorded()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        harness.Context.Organizations.Add(AnOrganization(organizationId, "Silent"));
        // API-metric and Blossom rows put the organization in the measure maps; the API rows
        // carry a WindowStart, so exclude them and use only the billing measure.
        harness.Context.DailyBillingMetrics.Add(ABillingMetric(organizationId, To.Date.AddDays(-1), 5m, 0.01m));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.BlossomUnits, limit: 10, cacheKey: null);

        var item = Assert.Single(result.Items);
        Assert.Null(item.DaysSinceLastActivity);
        Assert.True(result.DataQuality.LastActivityIsReconstructed);
    }

    [Fact]
    public async Task RankingEchoesTheMetricWindowAndTier()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        harness.Context.Organizations.Add(AnOrganization(organizationId, "Atelier", PlanTier.Rose));
        harness.Context.DailyAgentMetrics.Add(AnAgentMetric(organizationId, To.Date.AddDays(-1)));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.AgentRuns, limit: 10, cacheKey: null);

        Assert.Equal("agentRuns", result.Metric);
        Assert.Equal(To.AddDays(-7), result.From);
        Assert.Equal(To, result.To);
        Assert.Equal("Rose", result.Items[0].PlanTier);
        Assert.Equal("Atelier", result.Items[0].Name);
    }

    [Fact]
    public async Task RankingReturnsAnEmptyListRatherThanThrowingWhenNothingMatches()
    {
        var harness = Build();

        var result = await harness.Service.GetOrganizationUsageAsync(
            Window(7), BusinessRankingMetric.Messages, limit: 10, cacheKey: null);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }
}
