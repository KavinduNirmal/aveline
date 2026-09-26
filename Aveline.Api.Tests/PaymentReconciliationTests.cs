using System.Diagnostics.Metrics;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Jobs;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Plan §10 Phase 7 and §12.1's <c>aveline.payment.unreconciled_intents</c> /
/// <c>aveline.payment.webhook.unprocessed_backlog</c> gauges. The read and the alert must call
/// **one** derivation, for the reason the Blossom sibling gives: "a second derivation would let the
/// console and the alarm disagree about the same account" (<c>docs/api/README.md:981-982</c>).
/// </summary>
public class PaymentReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reconcile_ReportsAnIntentTheProviderSettledButAvelineDidNot()
    {
        using var fixture = new Fixture();
        var intent = await fixture.SeedIntentAsync(PaymentProviderStatus.RequiresAction);

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        var row = Assert.Single(report.UnreconciledIntents);
        Assert.Equal(intent.Id, row.PaymentIntentId);
        Assert.Equal(ReconciliationReasons.ProviderSettledUnconfirmed, row.Reason);
        Assert.Equal(nameof(PaymentProviderStatus.Succeeded), row.ProviderStatus);
        Assert.Equal(nameof(PaymentProviderStatus.RequiresAction), row.StoredStatus);
        Assert.Equal(1, report.UnreconciledByProvider["mock"]);
    }

    /// <summary>
    /// Acceptance criterion: "a deliberately unreconciled mock refund is reported by the
    /// reconciliation read and raises a metric". The refund is made at the provider and never
    /// recorded here, so the provider says the money came back and Aveline still believes it holds
    /// the charge.
    /// </summary>
    [Fact]
    public async Task Reconcile_ReportsADeliberatelyUnreconciledMockRefund()
    {
        using var fixture = new Fixture();
        var intent = await fixture.SeedIntentAsync(
            PaymentProviderStatus.Succeeded,
            settledAt: Now.UtcDateTime.AddHours(-2),
            refundAtProvider: true);

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        var row = Assert.Single(report.UnreconciledIntents);
        Assert.Equal(intent.Id, row.PaymentIntentId);
        Assert.Equal(ReconciliationReasons.RefundNotRecorded, row.Reason);
        Assert.Null(row.RefundedAt);
    }

    [Fact]
    public async Task Reconcile_ReportsARefundAvelineRecordedAndTheProviderNeverMade()
    {
        using var fixture = new Fixture();
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.Succeeded,
            settledAt: Now.UtcDateTime.AddHours(-2),
            refundedAt: Now.UtcDateTime.AddHours(-1));

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        var row = Assert.Single(report.UnreconciledIntents);
        Assert.Equal(ReconciliationReasons.RefundRecordedWithoutProvider, row.Reason);
    }

    [Fact]
    public async Task Reconcile_DoesNotReportAConsistentIntent()
    {
        using var fixture = new Fixture();
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.Succeeded, settledAt: Now.UtcDateTime.AddHours(-2));

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        Assert.Empty(report.UnreconciledIntents);
        Assert.Equal(1, report.IntentsChecked);
        Assert.Equal(0, report.UnreconciledCount);
    }

    [Fact]
    public async Task Reconcile_ReportsAnExpiredUnsettledIntent()
    {
        using var fixture = new Fixture();
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.RequiresAction,
            providerToken: MockPaymentProvider.RequiresActionToken,
            expiresAt: Now.UtcDateTime.AddMinutes(-5));

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        var row = Assert.Single(report.UnreconciledIntents);
        Assert.Equal(ReconciliationReasons.ExpiredUnsettled, row.Reason);
    }

    [Fact]
    public async Task Reconcile_ReportsAProviderIntentTheProviderDoesNotKnow()
    {
        using var fixture = new Fixture();
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.RequiresAction,
            providerToken: null,
            providerIntentId: "mock_orphaned");

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        var row = Assert.Single(report.UnreconciledIntents);
        Assert.Equal(ReconciliationReasons.ProviderUnknownIntent, row.Reason);
        Assert.Null(row.ProviderStatus);
    }

    [Fact]
    public async Task Reconcile_ReportsTheUnprocessedWebhookBacklog()
    {
        using var fixture = new Fixture();
        await fixture.SeedBacklogAsync("mock", "Provider event type 'Unknown' has no settlement effect in this phase.");
        await fixture.SeedBacklogAsync("mock", "A dispute event again.");

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        var row = Assert.Single(report.UnprocessedWebhookBacklog);
        Assert.Equal("mock", row.Provider);
        Assert.Equal(2, row.UnprocessedEvents);
        Assert.Equal(2, report.UnprocessedBacklogTotal);
        Assert.Equal(Now.UtcDateTime.AddHours(-1), row.OldestReceivedAt!.Value);
    }

    [Fact]
    public async Task Reconcile_IsScopedByOrganizationAndWindow()
    {
        using var fixture = new Fixture();
        var mine = await fixture.SeedIntentAsync(PaymentProviderStatus.RequiresAction);
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.RequiresAction,
            organizationId: Guid.CreateVersion7(),
            createdAt: Now.UtcDateTime.AddHours(-1));
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.RequiresAction,
            createdAt: Now.UtcDateTime.AddDays(-3));

        var scoped = await fixture.Service.ReconcileAsync(fixture.Query(organizationId: fixture.OrgId));

        Assert.Equal(mine.Id, Assert.Single(scoped.UnreconciledIntents).PaymentIntentId);

        var windowed = await fixture.Service.ReconcileAsync(fixture.Query(
            from: Now.UtcDateTime.AddHours(-2), to: Now.UtcDateTime));

        Assert.Equal(2, windowed.IntentsChecked);
    }

    [Fact]
    public async Task TheGauge_RisesForADeliberatelyUnreconciledMockRefund()
    {
        using var fixture = new Fixture();
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.Succeeded,
            settledAt: Now.UtcDateTime.AddHours(-2),
            refundAtProvider: true);

        var report = await fixture.Service.ReconcileAsync(fixture.Query());
        Assert.Equal(ReconciliationReasons.RefundNotRecorded, Assert.Single(report.UnreconciledIntents).Reason);

        var (unreconciled, _) = await ObserveGaugesAsync(
            () => fixture.Collector.RunAsync(CancellationToken.None));

        Assert.Equal(1, unreconciled["mock"]);
    }

    /// <summary>
    /// The read and the alert call one function. This asserts the consequence the Blossom sibling
    /// names: the number the endpoint reports and the number the gauge publishes come out equal,
    /// because both are the output of <see cref="IPaymentReconciliationService.ReconcileAsync"/>.
    /// </summary>
    [Fact]
    public async Task TheReadAndTheAlert_AgreeBecauseTheyCallOneFunction()
    {
        using var fixture = new Fixture();
        await fixture.SeedIntentAsync(PaymentProviderStatus.RequiresAction);
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.Succeeded,
            settledAt: Now.UtcDateTime.AddHours(-2),
            refundAtProvider: true);
        await fixture.SeedIntentAsync(
            PaymentProviderStatus.Succeeded, settledAt: Now.UtcDateTime.AddHours(-2));
        await fixture.SeedBacklogAsync("mock", "Provider event type 'Unknown' has no settlement effect.");
        await fixture.SeedBacklogAsync("mock", "A dispute event again.");

        var report = await fixture.Service.ReconcileAsync(fixture.Query());

        var (unreconciled, backlog) = await ObserveGaugesAsync(
            () => fixture.Collector.RunAsync(CancellationToken.None));

        Assert.Equal(2, report.UnreconciledCount);
        Assert.Equal(report.UnreconciledByProvider["mock"], unreconciled["mock"]);
        Assert.Equal(report.UnprocessedBacklogTotal, backlog["mock"]);
        Assert.Equal(2, unreconciled["mock"]);
        Assert.Equal(2, backlog["mock"]);
    }

    [Fact]
    public async Task TheAlert_ClearsAProviderGaugeThatFallsBackToZero()
    {
        using var fixture = new Fixture();
        var intent = await fixture.SeedIntentAsync(PaymentProviderStatus.RequiresAction);

        var (first, _) = await ObserveGaugesAsync(
            () => fixture.Collector.RunAsync(CancellationToken.None));
        Assert.Equal(1, first["mock"]);

        // The divergence is repaired: the row now matches the provider, so the next pass must
        // publish zero rather than leave the alert latched on a stale series.
        var row = await fixture.Context.PaymentIntents.SingleAsync(candidate => candidate.Id == intent.Id);
        row.Status = PaymentProviderStatus.Succeeded;
        row.SettledAt = Now.UtcDateTime;
        await fixture.Context.SaveChangesAsync();

        var (second, _) = await ObserveGaugesAsync(
            () => fixture.Collector.RunAsync(CancellationToken.None));
        Assert.Equal(0, second["mock"]);
    }

    private static async Task<(Dictionary<string, long> Unreconciled, Dictionary<string, long> Backlog)>
        ObserveGaugesAsync(Func<Task> act)
    {
        var unreconciled = new Dictionary<string, long>(StringComparer.Ordinal);
        var backlog = new Dictionary<string, long>(StringComparer.Ordinal);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Name is "aveline.payment.unreconciled_intents"
                or "aveline.payment.webhook.unprocessed_backlog")
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? provider = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "provider")
                {
                    provider = tag.Value?.ToString();
                }
            }

            var target = instrument.Name == "aveline.payment.unreconciled_intents" ? unreconciled : backlog;
            target[provider ?? string.Empty] = value;
        });
        listener.Start();

        await act();
        listener.RecordObservableInstruments();

        return (unreconciled, backlog);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubProviderFactory(IPaymentProvider provider) : IPaymentProviderFactory
    {
        public IPaymentProvider Active => provider;

        public IPaymentProvider Resolve(string providerKey) => provider;
    }

    /// <summary>In-memory database, a real mock adapter and the module's real service and job.</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: $"PaymentReconciliation_{Guid.NewGuid()}")
                .Options);

            var options = new PaymentsOptions { Currency = "LKR" };
            options.Mock.Enabled = true;
            options.Mock.AutoSettle = true;
            options.Mock.WebhookSigningSecret = "whsec_payment-reconciliation-test";

            Metrics = new PaymentMetrics();
            Provider = new MockPaymentProvider(
                Options.Create(options),
                new FixedClock(Now),
                NullLogger<MockPaymentProvider>.Instance,
                Metrics);

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            services.AddSingleton(Context);
            services.AddSingleton(Provider);
            services.AddSingleton<IPaymentProviderFactory>(new StubProviderFactory(Provider));
            services.AddSingleton(Metrics);
            services.AddSingleton<TimeProvider>(new FixedClock(Now));
            services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock());
            services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
            services.AddScoped<IPaymentIntentExpiryService, PaymentIntentExpiryService>();
            services.AddSingleton<PaymentReconciliationMetricCollectorJob>();

            Services = services.BuildServiceProvider();
            Service = Services.GetRequiredService<IPaymentReconciliationService>();
            Collector = Services.GetRequiredService<PaymentReconciliationMetricCollectorJob>();
        }

        public AppDbContext Context { get; }

        public MockPaymentProvider Provider { get; }

        public PaymentMetrics Metrics { get; }

        public ServiceProvider Services { get; }

        public IPaymentReconciliationService Service { get; }

        public PaymentReconciliationMetricCollectorJob Collector { get; }

        public Guid OrgId { get; } = Guid.CreateVersion7();

        public PaymentReconciliationQuery Query(
            Guid? organizationId = null, DateTime? from = null, DateTime? to = null) =>
            new(organizationId, Provider: "mock", From: from, To: to);

        public void Dispose()
        {
            Services.Dispose();
            Metrics.Dispose();
        }

        public async Task<PaymentIntent> SeedIntentAsync(
            PaymentProviderStatus storedStatus,
            string? providerToken = MockPaymentProvider.SucceedToken,
            DateTime? settledAt = null,
            DateTime? expiresAt = null,
            DateTime? refundedAt = null,
            bool refundAtProvider = false,
            Guid? organizationId = null,
            DateTime? createdAt = null,
            string? providerIntentId = null)
        {
            var intentId = Guid.CreateVersion7();
            ProviderPaymentIntent? providerIntent = null;

            if (providerToken is not null)
            {
                providerIntent = await Provider.CreatePaymentIntentAsync(
                    new CreateProviderIntentRequest(
                        intentId,
                        Money.Lkr(35m),
                        PaymentPurpose.BlossomTopUp,
                        "Reconciliation test intent.",
                        OrgId.ToString(),
                        intentId.ToString(),
                        null,
                        expiresAt),
                    providerToken);

                if (refundAtProvider)
                {
                    await Provider.RefundAsync(new ProviderRefundRequest(
                        providerIntent.ProviderIntentId,
                        providerIntent.Amount,
                        $"recon-refund-{intentId:N}",
                        "Deliberately unreconciled reconciliation-test refund."));
                }
            }

            var intent = new PaymentIntent
            {
                Id = intentId,
                OrganizationId = organizationId ?? OrgId,
                Provider = "mock",
                ProviderIntentId = providerIntentId ?? providerIntent?.ProviderIntentId ?? $"mock_{intentId:N}",
                ExternalRef = providerIntent?.ProviderIntentId,
                Purpose = PaymentPurpose.BlossomTopUp,
                Status = storedStatus,
                AmountMinor = 3500,
                Currency = "LKR",
                PriceLkr = 35m,
                SkuCode = "pack_500",
                BlossomQuantity = 500m,
                Description = "Reconciliation test intent.",
                CreatedAt = createdAt ?? Now.UtcDateTime.AddHours(-1),
                UpdatedAt = createdAt ?? Now.UtcDateTime.AddHours(-1),
                SettledAt = settledAt,
                ExpiresAt = expiresAt,
                RefundedAt = refundedAt,
            };

            Context.PaymentIntents.Add(intent);
            await Context.SaveChangesAsync();
            return intent;
        }

        public async Task SeedBacklogAsync(string provider, string processingError)
        {
            Context.PaymentProviderEvents.Add(new PaymentProviderEvent
            {
                Provider = provider,
                ProviderEventId = $"evt_unprocessed_{Guid.NewGuid():N}",
                EventType = PaymentWebhookEventType.Unknown,
                ProviderIntentId = "mock_whatever",
                OccurredAt = Now.UtcDateTime.AddHours(-1),
                ReceivedAt = Now.UtcDateTime.AddHours(-1),
                RawPayload = "{}",
                ProcessedAt = null,
                ProcessingError = processingError,
            });

            await Context.SaveChangesAsync();
        }
    }
}
