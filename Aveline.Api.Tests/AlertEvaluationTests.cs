using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #229 — the alert evaluation service. Covers fire, cooldown aggregation (BR-7.6),
/// auto-resolution after the configured consecutive OK evaluations (BR-7.7), the critical
/// notification (BR-7.11), acknowledgement with audit, and the <c>blossom.ledger.drift</c>
/// acceptance case.
/// </summary>
public class AlertEvaluationTests
{
    private static readonly Guid DriftRuleId = Guid.Parse("5a8632d2-648e-4af7-a7e3-15cb9251f9d1");

    [Fact]
    public async Task BlossomLedgerDrift_SeededRuleFiresOnANonZeroProjection()
    {
        var harness = Build();
        await SeedAsync(harness, SystemAlertRuleSeed.Rules.ToArray());
        await SeedSampleAsync(harness, "aveline.blossom.reconciliation.drift", 0.25m);

        using (var scope = harness.Provider.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAlertService>().EvaluateAsync() > 0);
        }

        await using var context = harness.Context();
        var alert = await context.SystemAlerts.SingleAsync();
        Assert.Equal(DriftRuleId, alert.RuleId);
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(0.25m, alert.ObservedValue);
        Assert.Contains("system.alert.fired", harness.EventBus.PublishedEventTypes);
    }

    [Fact]
    public async Task DoesNotFireWhileTheAggregateStaysBelowTheThreshold()
    {
        var harness = Build();
        await SeedAsync(harness, Rule("test.low", AlertAggregation.Max, AlertComparisonOperator.Gt, 10m));
        await SeedSampleAsync(harness, "test.low", 5m);

        using (var scope = harness.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAlertService>().EvaluateAsync();
        }

        await using var context = harness.Context();
        Assert.Equal(0, await context.SystemAlerts.CountAsync());
    }

    [Fact]
    public async Task Cooldown_AggregatesOccurrencesInsteadOfFiringAgain()
    {
        var harness = Build();
        await SeedAsync(harness, Rule("test.cooldown", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m));
        await SeedSampleAsync(harness, "test.cooldown", 1m);

        using (var scope = harness.Provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAlertService>();
            await service.EvaluateAsync();
            await service.EvaluateAsync();
        }

        await using var context = harness.Context();
        var alert = await context.SystemAlerts.SingleAsync();
        Assert.Equal(2, alert.OccurrenceCount);
        Assert.Equal(AlertStatus.Firing, alert.Status);
    }

    [Fact]
    public async Task Cooldown_ReFiresAfterItElapsesAndKeepsAggregatingInsideIt()
    {
        var harness = Build();
        var rule = Rule("test.cooldown.refire", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m);
        await SeedAsync(harness, rule);
        await SeedSampleAsync(harness, "test.cooldown.refire", 1m);

        await EvaluateOnceAsync(harness); // first fire
        await EvaluateOnceAsync(harness); // sustained breach inside the cooldown

        await using (var context = harness.Context())
        {
            var open = await context.SystemAlerts.SingleAsync();
            Assert.Equal(2, open.OccurrenceCount);
        }

        // Move the fire time outside the cooldown, as a sustained breach would.
        await using (var context = harness.Context())
        {
            var open = await context.SystemAlerts.SingleAsync();
            open.FiredAt = DateTime.UtcNow.AddSeconds(-(rule.CooldownSeconds + 1));
            await context.SaveChangesAsync();
        }

        await EvaluateOnceAsync(harness); // cooldown elapsed: re-fire

        await using var verify = harness.Context();
        var alert = await verify.SystemAlerts.SingleAsync();
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal(1, alert.OccurrenceCount);
        Assert.True(
            alert.FiredAt > DateTime.UtcNow.AddSeconds(-30),
            "the re-fire must reset FiredAt to the evaluation time");
        Assert.Equal(2, harness.EventBus.PublishedEventTypes.Count(type => type == "system.alert.fired"));
    }

    [Fact]
    public async Task BlossomLedgerDrift_FiresFromASampleProducedByTheCollector()
    {
        var harness = Build();
        await SeedAsync(harness, SystemAlertRuleSeed.Rules.ToArray());

        // A period account whose cached projection does not match its ledger-derived balance.
        await using (var context = harness.Context())
        {
            context.UsageAccounts.Add(new UsageAccount
            {
                OrganizationId = Guid.CreateVersion7(),
                PeriodStart = DateTime.UtcNow.AddDays(-1),
                PeriodEnd = DateTime.UtcNow.AddDays(29),
                MonthlyBlossomLimit = 100m,
                BlossomUsed = 40m,
                BlossomRemaining = 50m, // 100 - 40 = 60 derived; drift -10
            });
            await context.SaveChangesAsync();
        }

        var snapshot = await CollectRealSnapshotAsync(harness);
        await using (var context = harness.Context())
        {
            context.SystemMetricSamples.AddRange(SystemMetricCollector.BuildSamples(snapshot));
            await context.SaveChangesAsync();
        }

        using (var scope = harness.Provider.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAlertService>().EvaluateAsync() > 0);
        }

        await using var verify = harness.Context();
        var alert = await verify.SystemAlerts.SingleAsync();
        Assert.Equal(DriftRuleId, alert.RuleId);
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.True(alert.ObservedValue > 0m);
    }

    [Fact]
    public async Task AutoResolvesAfterThreeConsecutiveBelowThresholdEvaluations()
    {
        var harness = Build();
        await SeedAsync(harness, Rule("test.resolve", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m));
        await SeedSampleAsync(harness, "test.resolve", 1m);

        using (var scope = harness.Provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAlertService>();
            await service.EvaluateAsync();

            await ReplaceSamplesAsync(harness, "test.resolve", 0m);

            await service.EvaluateAsync();
            await service.EvaluateAsync();

            await using (var mid = harness.Context())
            {
                var pending = await mid.SystemAlerts.SingleAsync();
                Assert.Equal(AlertStatus.Firing, pending.Status);
                Assert.Equal(2, pending.ConsecutiveOkCount);
            }

            await service.EvaluateAsync();
        }

        await using var context = harness.Context();
        var alert = await context.SystemAlerts.SingleAsync();
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.NotNull(alert.ResolvedAt);
        Assert.Contains("system.alert.resolved", harness.EventBus.PublishedEventTypes);
    }

    [Fact]
    public async Task CriticalAlert_CreatesANotificationRecordForResolvedRecipients()
    {
        var harness = Build();
        var organizationId = Guid.CreateVersion7();
        var rule = Rule(
            "test.notify", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m,
            AlertSeverity.Critical);
        await SeedAsync(harness, rule);
        await SeedSampleAsync(harness, "test.notify", 1m);
        harness.Recipients.Recipients =
        [
            new ResolvedRecipient(Guid.CreateVersion7(), "owner@aveline.lk", true, default, []),
        ];

        SystemAlert? alert;
        using (var scope = harness.Provider.CreateScope())
        {
            alert = await scope.ServiceProvider.GetRequiredService<IAlertService>()
                .EvaluateRuleAsync(rule, organizationId);
        }

        Assert.NotNull(alert);
        Assert.NotNull(alert!.NotificationRecordId);

        await using var context = harness.Context();
        var record = await context.NotificationRecords.SingleAsync();
        Assert.Equal(alert.NotificationRecordId, record.Id);
        Assert.Equal(organizationId, record.OrganizationId);
        Assert.Equal(NotificationType.SystemAlert, record.Type);
    }

    [Fact]
    public async Task WarningAlert_DoesNotCreateANotification()
    {
        var harness = Build();
        var rule = Rule("test.warn", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m);
        await SeedAsync(harness, rule);
        await SeedSampleAsync(harness, "test.warn", 1m);
        harness.Recipients.Recipients =
        [
            new ResolvedRecipient(Guid.CreateVersion7(), "owner@aveline.lk", true, default, []),
        ];

        using (var scope = harness.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAlertService>()
                .EvaluateRuleAsync(rule, Guid.CreateVersion7());
        }

        await using var context = harness.Context();
        Assert.Equal(0, await context.NotificationRecords.CountAsync());
    }

    [Fact]
    public async Task Acknowledge_SetsStatusAndWritesAuditAndEvent()
    {
        var harness = Build();
        await SeedAsync(harness, Rule("test.ack", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m));
        await SeedSampleAsync(harness, "test.ack", 1m);

        Guid alertId;
        using (var scope = harness.Provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAlertService>();
            await service.EvaluateAsync();

            await using var context = harness.Context();
            alertId = (await context.SystemAlerts.SingleAsync()).Id;
        }

        var userId = Guid.CreateVersion7();
        using (var scope = harness.Provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAlertService>();
            var acknowledged = await service.AcknowledgeAsync(alertId, userId, "looking into it");

            Assert.NotNull(acknowledged);
            Assert.Equal(AlertStatus.Acknowledged, acknowledged!.Status);
            Assert.Equal(userId, acknowledged.AcknowledgedByUserId);
            Assert.NotNull(acknowledged.AcknowledgedAt);
        }

        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "system.alert.acknowledged");
        Assert.Contains("system.alert.acknowledged", harness.EventBus.PublishedEventTypes);
    }

    [Fact]
    public async Task Acknowledge_UnknownAlertReturnsNull()
    {
        var harness = Build();

        using var scope = harness.Provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAlertService>()
            .AcknowledgeAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Null(result);
    }

    [Fact]
    public async Task RuleCrud_WritesAuditEntries()
    {
        var harness = Build();
        var rule = Rule("test.crud", AlertAggregation.Max, AlertComparisonOperator.Gt, 1m);
        var actor = Guid.CreateVersion7();

        using (var scope = harness.Provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAlertService>();
            var created = await service.CreateRuleAsync(rule, actor);
            created.Threshold = 2m;
            await service.UpdateRuleAsync(created, actor);
            Assert.True(await service.DeleteRuleAsync(created.Id, actor));
        }

        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "system.alert_rule.created");
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "system.alert_rule.updated");
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "system.alert_rule.deleted");
    }

    private static async Task<int> EvaluateOnceAsync(Harness harness)
    {
        using var scope = harness.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAlertService>().EvaluateAsync();
    }

    [Fact]
    public async Task ReFire_IsCappedByTheMaxAlertsPerHourStormGuard()
    {
        var harness = Build();
        var rule = Rule("test.refire.guard", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m);
        rule.MaxAlertsPerHour = 2;
        await SeedAsync(harness, rule);
        await SeedSampleAsync(harness, "test.refire.guard", 1m);

        await EvaluateOnceAsync(harness); // fire 1 consumes the first quota slot

        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Age the last fire past the cooldown while the breach continues, which is
            // exactly the path that used to bypass the storm guard (§3.3(a)).
            await using (var context = harness.Context())
            {
                var open = await context.SystemAlerts.SingleAsync();
                open.FiredAt = DateTime.UtcNow.AddSeconds(-(rule.CooldownSeconds + 1));
                await context.SaveChangesAsync();
            }

            await EvaluateOnceAsync(harness);
        }

        var fires = harness.EventBus.PublishedEventTypes.Count(type => type == "system.alert.fired");
        Assert.Equal(2, fires); // the initial fire plus a single re-fire inside the hour
    }

    [Fact]
    public async Task ReFire_RefreshesTheRulesLastTriggeredAt()
    {
        var harness = Build();
        var rule = Rule("test.refire.stamp", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m);
        await SeedAsync(harness, rule);
        await SeedSampleAsync(harness, "test.refire.stamp", 1m);

        await EvaluateOnceAsync(harness);
        DateTime? firstTrigger;
        await using (var context = harness.Context())
        {
            firstTrigger = (await context.SystemAlertRules.SingleAsync()).LastTriggeredAt;
        }

        await using (var context = harness.Context())
        {
            var open = await context.SystemAlerts.SingleAsync();
            open.FiredAt = DateTime.UtcNow.AddSeconds(-(rule.CooldownSeconds + 1));
            await context.SaveChangesAsync();
        }

        await EvaluateOnceAsync(harness);

        await using (var context = harness.Context())
        {
            var refreshed = (await context.SystemAlertRules.SingleAsync()).LastTriggeredAt;
            Assert.NotNull(refreshed);
            Assert.True(refreshed > firstTrigger, "a re-fire must refresh rule.LastTriggeredAt");
        }
    }

    [Fact]
    public async Task MissingSamples_DoNotAutoResolveAFiringAlert()
    {
        var harness = Build();
        await SeedAsync(harness, Rule("test.gap", AlertAggregation.Max, AlertComparisonOperator.Gt, 0m));
        await SeedSampleAsync(harness, "test.gap", 1m);
        await EvaluateOnceAsync(harness);

        // A telemetry gap removes the sample entirely; that is not evidence of health.
        await using (var context = harness.Context())
        {
            context.SystemMetricSamples.RemoveRange(context.SystemMetricSamples);
            await context.SaveChangesAsync();
        }

        for (var pass = 0; pass < 5; pass++)
        {
            await EvaluateOnceAsync(harness);
        }

        await using var verify = harness.Context();
        var alert = await verify.SystemAlerts.SingleAsync();
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal(0, alert.ConsecutiveOkCount);
        Assert.DoesNotContain("system.alert.resolved", harness.EventBus.PublishedEventTypes);
    }

    [Fact]
    public async Task BlossomBalance_IgnoresClosedHistoricalPeriods()
    {
        var harness = Build();
        await using (var context = harness.Context())
        {
            // A closed, overdrawn historical period. Closed rows are never deleted, so an
            // unfiltered Min(...) would latch the Critical negative-balance alert (§3.3(b)).
            context.UsageAccounts.Add(new UsageAccount
            {
                OrganizationId = Guid.CreateVersion7(),
                PeriodStart = DateTime.UtcNow.AddDays(-60),
                PeriodEnd = DateTime.UtcNow.AddDays(-30),
                IsClosed = true,
                ClosedAt = DateTime.UtcNow.AddDays(-30),
                MonthlyBlossomLimit = 100m,
                BlossomUsed = 150m,
                BlossomRemaining = -50m,
            });
            context.UsageAccounts.Add(new UsageAccount
            {
                OrganizationId = Guid.CreateVersion7(),
                PeriodStart = DateTime.UtcNow.AddDays(-1),
                PeriodEnd = DateTime.UtcNow.AddDays(29),
                MonthlyBlossomLimit = 100m,
                BlossomUsed = 10m,
                BlossomRemaining = 90m,
            });
            await context.SaveChangesAsync();
        }

        var snapshot = await CollectRealSnapshotAsync(harness);

        Assert.Equal(90m, snapshot.BlossomBalance);
    }

    [Fact]
    public async Task DatabaseCaptureFailure_IsCountedInsteadOfSwallowed()
    {
        var services = new ServiceCollection();
        var disposed = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Alerts_{Guid.NewGuid()}")
            .Options);
        disposed.Dispose();
        services.AddSingleton(disposed);
        using var provider = services.BuildServiceProvider();

        var collector = new RealCaptureCollector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock());

        var snapshot = await collector.CaptureForTestAsync(provider, CancellationToken.None);

        Assert.Equal(1, collector.DatabaseReadFailures);
        Assert.Null(snapshot.BlossomReconciliationDrift);
    }

    private static async Task<MetricSnapshot> CollectRealSnapshotAsync(Harness harness)
    {
        var collector = new RealCaptureCollector(
            harness.Provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock());

        using var scope = harness.Provider.CreateScope();
        return await collector.CaptureForTestAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static SystemAlertRule Rule(
        string name,
        AlertAggregation aggregation,
        AlertComparisonOperator comparison,
        decimal threshold,
        AlertSeverity severity = AlertSeverity.Warning) => new()
    {
        Name = name,
        MetricName = name,
        Aggregation = aggregation,
        ComparisonOperator = comparison,
        Threshold = threshold,
        WindowSeconds = 300,
        Severity = severity,
        IsEnabled = true,
        CooldownSeconds = 300,
        MaxAlertsPerHour = 10,
        TargetRoles = ["org:boutique_owner"],
        CreatedByUserId = Guid.Empty,
    };

    private static Harness Build()
    {
        var databaseName = $"Alerts_{Guid.NewGuid()}";
        var eventBus = new RecordingEventBus();
        var audit = new RecordingAuditService();
        var recipients = new StubRecipientResolver();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:AutoResolveConsecutiveOk"] = "3",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<IAuditService>(audit);
        services.AddSingleton<IRecipientResolver>(recipients);
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IAlertService, AlertService>();

        return new Harness(services.BuildServiceProvider(), databaseName, eventBus, audit, recipients);
    }

    private static async Task SeedAsync(Harness harness, params SystemAlertRule[] rules)
    {
        await using var context = harness.Context();
        context.SystemAlertRules.AddRange(rules);
        await context.SaveChangesAsync();
    }

    private static async Task SeedSampleAsync(Harness harness, string metricName, decimal value)
    {
        await using var context = harness.Context();
        context.SystemMetricSamples.Add(Sample(metricName, value));
        await context.SaveChangesAsync();
    }

    private static async Task ReplaceSamplesAsync(Harness harness, string metricName, decimal value)
    {
        await using var context = harness.Context();
        context.SystemMetricSamples.RemoveRange(context.SystemMetricSamples);
        context.SystemMetricSamples.Add(Sample(metricName, value));
        await context.SaveChangesAsync();
    }

    private static SystemMetricSample Sample(string metricName, decimal value) => new()
    {
        MetricName = metricName,
        DimensionsJson = "{}",
        DimensionHash = new string('a', 64),
        ValueDecimal = value,
        Unit = "count",
        WindowStart = DateTime.UtcNow.AddSeconds(-5),
        WindowSize = "instant",
        SampledAt = DateTime.UtcNow.AddSeconds(-5),
    };

    private sealed record Harness(
        ServiceProvider Provider,
        string DatabaseName,
        RecordingEventBus EventBus,
        RecordingAuditService Audit,
        StubRecipientResolver Recipients)
    {
        public AppDbContext Context() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DatabaseName).Options);
    }

    private sealed class RealCaptureCollector(
        IServiceScopeFactory scopeFactory,
        IDistributedJobLock jobLock)
        : SystemMetricCollector(
            scopeFactory,
            jobLock,
            NullLogger<SystemMetricCollector>.Instance,
            new ConfigurationBuilder().Build())
    {
        public Task<MetricSnapshot> CaptureForTestAsync(
            IServiceProvider services, CancellationToken cancellationToken)
            => base.CaptureAsync(services, cancellationToken);
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<string> PublishedEventTypes { get; } = [];

        public Task PublishAsync(
            string eventType, Guid? organizationId, object? payload, Guid? traceId = null,
            CancellationToken cancellationToken = default)
        {
            PublishedEventTypes.Add(eventType);
            return Task.CompletedTask;
        }

        public Task SubscribeAsync(
            string eventType, Func<EventEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UnsubscribeAsync(
            string eventType, Func<EventEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntryRequest> Entries { get; } = [];

        public Task RecordAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            Entries.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class StubRecipientResolver : IRecipientResolver
    {
        public IReadOnlyList<ResolvedRecipient> Recipients { get; set; } = [];

        public Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
            Notification notification, CancellationToken cancellationToken = default)
            => Task.FromResult(Recipients);
    }
}
