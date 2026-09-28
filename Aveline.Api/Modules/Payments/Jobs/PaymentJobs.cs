using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Billing.Jobs;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Modules.Payments.Jobs;

/// <summary>
/// The intent-expiry sweep of plan §10 Phase 7 (§6.6's "Re-listed in Phase 7"). A periodic pass that
/// makes the derivable <c>Expired</c> state durable.
/// </summary>
/// <remarks>
/// It rides <see cref="LedgerJobBase"/>, the repository's scheduled-job pattern (per-run scope plus
/// <see cref="IDistributedJobLock"/>), so a multi-instance deployment does not sweep twice and
/// <c>RunAsync</c> can be driven deterministically by a test.
/// </remarks>
public sealed class PaymentIntentExpiryJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<PaymentIntentExpiryJob> logger)
    : LedgerJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "payment-intent-expiry";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sweep = scope.ServiceProvider.GetRequiredService<IPaymentIntentExpiryService>();
        return await sweep.SweepAsync(cancellationToken);
    }
}

/// <summary>
/// The unreconciled-webhook alert (plan §10 Phase 7, §12.1). One pass resolves
/// <see cref="IPaymentReconciliationService"/> — the same derivation the reconciliation read serves —
/// and publishes <c>aveline.payment.unreconciled_intents</c> and
/// <c>aveline.payment.webhook.unprocessed_backlog</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the collector and not the endpoint.</b> The read must not perform work; the alert must keep
/// firing while nobody is looking at the console. Publishing here, from the shared derivation, is
/// what makes "the read and the alert cannot disagree" true by construction rather than by
/// discipline (the Blossom drift precedent).
/// </para>
/// <para>
/// <b>Clearing a series.</b> A provider that drops out of the window entirely would otherwise leave
/// its last value latched, and an alert that fires "above zero for longer than the webhook SLA" must
/// be able to resolve. Each pass therefore also writes zero for every provider it published on the
/// previous pass and did not see this time.
/// </para>
/// </remarks>
public sealed class PaymentReconciliationMetricCollectorJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<PaymentReconciliationMetricCollectorJob> logger)
    : LedgerJobBase(scopeFactory, jobLock, logger)
{
    private readonly HashSet<string> _publishedUnreconciled = new(StringComparer.Ordinal);
    private readonly HashSet<string> _publishedBacklog = new(StringComparer.Ordinal);

    protected override string JobName => "payment-reconciliation-collector";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var reconciliation = scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>();
        var metrics = scope.ServiceProvider.GetRequiredService<PaymentMetrics>();

        var report = await reconciliation.ReconcileAsync(
            cancellationToken: cancellationToken);

        Publish(metrics, report);

        return report.UnreconciledCount + (int)report.UnprocessedBacklogTotal;
    }

    /// <summary>
    /// Publishes one report's gauges. Internal so the read/alert agreement test can drive a pass
    /// without a host.
    /// </summary>
    internal void Publish(PaymentMetrics metrics, PaymentReconciliationReport report)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(report);

        var unreconciledProviders = new HashSet<string>(report.UnreconciledByProvider.Keys, StringComparer.Ordinal);
        foreach (var pair in report.UnreconciledByProvider)
        {
            metrics.SetUnreconciledIntents(pair.Key, pair.Value);
        }

        foreach (var stale in _publishedUnreconciled.Except(unreconciledProviders).ToArray())
        {
            metrics.SetUnreconciledIntents(stale, 0);
        }

        _publishedUnreconciled.Clear();
        _publishedUnreconciled.UnionWith(unreconciledProviders);

        var backlogProviders = new HashSet<string>(
            report.UnprocessedWebhookBacklog.Select(row => row.Provider), StringComparer.Ordinal);

        foreach (var row in report.UnprocessedWebhookBacklog)
        {
            metrics.SetWebhookUnprocessedBacklog(row.Provider, row.UnprocessedEvents);
        }

        foreach (var stale in _publishedBacklog.Except(backlogProviders).ToArray())
        {
            metrics.SetWebhookUnprocessedBacklog(stale, 0);
        }

        _publishedBacklog.Clear();
        _publishedBacklog.UnionWith(backlogProviders);
    }
}
