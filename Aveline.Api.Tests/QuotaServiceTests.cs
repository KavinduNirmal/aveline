using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Statistics.Services;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #224 — quota window arithmetic (including month-end), limit 0 as unlimited,
/// once-per-period warning/exhaustion events, and the enforcement flag.
/// </summary>
public class QuotaServiceTests
{
    private sealed class FixedEntitlements(IReadOnlyDictionary<string, decimal> limits) : IEntitlementResolver
    {
        public Task<EntitlementValue?> GetAsync(
            Guid organizationId, string key, DateTime? at = null, CancellationToken cancellationToken = default)
            => Task.FromResult<EntitlementValue?>(null);

        public Task<IReadOnlyDictionary<string, EntitlementValue>> GetAllAsync(
            Guid organizationId, DateTime? at = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, EntitlementValue>>(
                new Dictionary<string, EntitlementValue>());

        public Task<decimal> GetDecimalAsync(
            Guid organizationId, string key, decimal fallback, DateTime? at = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(limits.TryGetValue(key, out var limit) ? limit : fallback);

        public Task<decimal> GetTierDecimalAsync(
            PlanTier tier, string key, decimal fallback, DateTime? at = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(limits.TryGetValue(key, out var limit) ? limit : fallback);
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<(string Type, Guid? OrganizationId, object? Payload)> Published { get; } = [];

        public Task PublishAsync(
            string eventType, Guid? organizationId, object? payload, Guid? traceId = null,
            CancellationToken cancellationToken = default)
        {
            Published.Add((eventType, organizationId, payload));
            return Task.CompletedTask;
        }

        public Task SubscribeAsync(
            string eventType, Func<EventEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UnsubscribeAsync(
            string eventType, Func<EventEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (QuotaService Service, RecordingEventBus Bus) Build(
        decimal monthly, decimal perMinute = 0, bool enforced = true, int warningPercent = 80)
    {
        var limits = new Dictionary<string, decimal>
        {
            [QuotaService.MonthlyMetricKey] = monthly,
            [QuotaService.PerMinuteMetricKey] = perMinute,
        };

        var bus = new RecordingEventBus();
        var service = new QuotaService(
            new FixedEntitlements(limits),
            new InMemoryQuotaCounterStore(),
            bus,
            Options.Create(new TelemetryOptions { QuotaWarningPercent = warningPercent }),
            Options.Create(new QuotaOptions { EnforcementEnabled = enforced }),
            NullLogger<QuotaService>.Instance);

        return (service, bus);
    }

    [Fact]
    public void MonthlyPeriodEndsAtTheNextMonthBoundary()
    {
        var (start, end) = QuotaService.ResolvePeriod(
            QuotaService.MonthlyMetricKey, new DateTime(2026, 1, 31, 23, 59, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void DecemberMonthlyPeriodRollsIntoTheNextYear()
    {
        var (_, end) = QuotaService.ResolvePeriod(
            QuotaService.MonthlyMetricKey, new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void PerMinutePeriodIsTheUtcMinute()
    {
        var (start, end) = QuotaService.ResolvePeriod(
            QuotaService.PerMinuteMetricKey, new DateTime(2026, 9, 11, 10, 15, 42, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 11, 10, 15, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 9, 11, 10, 16, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public async Task LimitZeroMeansNotConfiguredAndUnlimited()
    {
        var (service, _) = Build(monthly: 0);
        var organizationId = Guid.CreateVersion7();

        for (var i = 0; i < 5; i++)
        {
            var evaluation = await service.EvaluateAsync(organizationId, null);
            Assert.False(evaluation.IsExhausted);
            Assert.Empty(evaluation.Checks);
        }
    }

    [Fact]
    public async Task WarningFiresExactlyOncePerPeriod()
    {
        var (service, bus) = Build(monthly: 100, warningPercent: 80);
        var organizationId = Guid.CreateVersion7();

        for (var i = 0; i < 80; i++)
        {
            await service.EvaluateAsync(organizationId, null);
        }

        Assert.Single(bus.Published, e => e.Type == QuotaService.WarningEvent);
        Assert.DoesNotContain(bus.Published, e => e.Type == QuotaService.ExhaustedEvent);
    }

    [Fact]
    public async Task ExhaustionBlocksAndFiresExactlyOnce()
    {
        var (service, bus) = Build(monthly: 2);
        var organizationId = Guid.CreateVersion7();

        var first = await service.EvaluateAsync(organizationId, null);
        Assert.False(first.IsExhausted);

        var second = await service.EvaluateAsync(organizationId, null);
        Assert.True(second.IsExhausted);
        Assert.Equal(2, second.Exhausted!.Limit);
        Assert.Equal(2, second.Exhausted.Used);

        await service.EvaluateAsync(organizationId, null);
        Assert.Single(bus.Published, e => e.Type == QuotaService.ExhaustedEvent);
    }

    [Fact]
    public async Task DisabledEnforcementStillMeasuresButDoesNotBlock()
    {
        var (service, _) = Build(monthly: 1, enforced: false);
        var organizationId = Guid.CreateVersion7();

        Assert.False(service.EnforcementEnabled);
        var evaluation = await service.EvaluateAsync(organizationId, null);

        Assert.False(evaluation.Enforced);
        Assert.True(evaluation.IsExhausted);
        var check = Assert.Single(evaluation.Checks);
        Assert.Equal(1, check.Used);
    }

    [Fact]
    public async Task StatusReadDoesNotConsume()
    {
        var (service, _) = Build(monthly: 10);
        var organizationId = Guid.CreateVersion7();

        await service.EvaluateAsync(organizationId, null);
        var status = await service.GetStatusAsync(organizationId, null);

        Assert.Equal(1, Assert.Single(status.Checks).Used);
    }
}
