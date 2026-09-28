using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The reconciliation read of plan §10 Phase 7: provider state against <c>PaymentIntents</c> for a
/// window, plus the P6 unprocessed-webhook backlog.
/// </summary>
/// <remarks>
/// <para>
/// <b>One derivation.</b> This class is the payment analogue of the Blossom drift read
/// (<c>Modules/Billing/Endpoints/BlossomReconciliationEndpoints.cs</c>), and for the same reason:
/// "a second derivation would let the console and the alarm disagree about the same account"
/// (<c>docs/api/README.md:981-982</c>). The HTTP route and
/// <c>PaymentReconciliationMetricCollectorJob</c> — the alert — both call
/// <see cref="ReconcileAsync"/> and nothing else; neither re-implements a rule.
/// </para>
/// <para>
/// <b>What "unreconciled" means.</b> A row is reported when the two sides disagree in a way that
/// matters: the provider settled a charge the row still calls pending (a lost webhook, risk R7), the
/// two statuses differ, the amount or currency differ (risks R5/R9), the provider does not know the
/// charge, the adapter cannot be resolved at all (which is <em>not</em> an all-clear), the row is
/// past its expiry without having settled, or a refund exists on one side only.
/// </para>
/// <para>
/// <b>Bounded.</b> The scan is capped at <see cref="MaxIntentsScanned"/> rows per pass and the report
/// says when the cap bit, so a large window degrades visibly rather than silently.
/// </para>
/// </remarks>
public sealed class PaymentReconciliationService(
    AppDbContext db,
    IPaymentProviderFactory providers,
    TimeProvider clock,
    ILogger<PaymentReconciliationService> logger) : IPaymentReconciliationService
{
    /// <summary>The default window, shared with the alert so an argument-less read agrees with it.</summary>
    public const int DefaultWindowHours = 24;

    /// <summary>Upper bound on intents inspected in one pass; a larger window reports `Truncated`.</summary>
    public const int MaxIntentsScanned = 200;

    /// <summary>Upper bound on distinct processing errors carried into the report.</summary>
    private const int MaxBacklogReasons = 5;

    public async Task<PaymentReconciliationReport> ReconcileAsync(
        PaymentReconciliationQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new PaymentReconciliationQuery();

        var now = clock.GetUtcNow().UtcDateTime;
        var to = query.To ?? now;
        var from = query.From ?? to.AddHours(-DefaultWindowHours);

        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query), "The reconciliation window's lower bound is after its upper bound.");
        }

        var candidates = await db.PaymentIntents
            .AsNoTracking()
            .Where(intent => intent.CreatedAt >= from && intent.CreatedAt <= to)
            .Where(intent => query.OrganizationId == null || intent.OrganizationId == query.OrganizationId)
            .Where(intent => query.Provider == null || intent.Provider == query.Provider)
            // Newest first, so a truncated scan inspects the charges an operator is most likely to
            // be asking about rather than the oldest ones in the window.
            .OrderByDescending(intent => intent.CreatedAt)
            .Take(MaxIntentsScanned + 1)
            .ToListAsync(cancellationToken);

        var truncated = candidates.Count > MaxIntentsScanned;
        if (truncated)
        {
            candidates = candidates.Take(MaxIntentsScanned).ToList();
        }

        var rows = new List<UnreconciledIntentRow>();
        var unreconciledByProvider = new Dictionary<string, long>(StringComparer.Ordinal);
        var unavailableProviders = new List<string>();
        var refundComparisonUnsupported = new List<string>();

        foreach (var group in candidates.GroupBy(intent => intent.Provider, StringComparer.Ordinal))
        {
            long unreconciled = 0;
            IPaymentProvider? provider = null;

            try
            {
                provider = providers.Resolve(group.Key);
            }
            catch (PaymentDomainException exception)
            {
                // Not an all-clear: an adapter that cannot be resolved cannot confirm anything, so
                // every intent under it is reported. The alternative -- omitting them -- would let a
                // misconfiguration read as a healthy account.
                unavailableProviders.Add(group.Key);
                logger.LogWarning(
                    exception,
                    "Payment reconciliation could not resolve provider {Provider}; its intents are "
                    + "reported unreconciled.",
                    group.Key);
            }

            if (provider is not null && provider is not IPaymentRefundReader)
            {
                refundComparisonUnsupported.Add(group.Key);
            }

            foreach (var intent in group)
            {
                var row = provider is null
                    ? Describe(intent, providerStatus: null, ReconciliationReasons.ProviderUnavailable, now)
                    : await InspectAsync(intent, provider, now, cancellationToken);

                if (row is null)
                {
                    continue;
                }

                rows.Add(row);
                unreconciled++;
            }

            // Zero is published as well as a positive count, so a repaired provider clears its gauge
            // instead of leaving the alert latched on a stale series.
            unreconciledByProvider[group.Key] = unreconciled;
        }

        var backlog = await ReadBacklogAsync(query.Provider, cancellationToken);
        var backlogTotal = backlog.Sum(row => row.UnprocessedEvents);

        var notes = new List<string>
        {
            "An intent is reported when the provider's charge state and the stored row disagree: a "
            + "charge the provider settled that the row still calls pending (a webhook that never "
            + "arrived), a differing status, amount or currency, a charge the provider does not know, "
            + "a refund recorded on one side only, or an adapter that could not be resolved. An "
            + "unresolvable adapter is reported, never treated as an all-clear.",
            "This read does not repair anything. It is the same derivation the "
            + "unreconciled_intents gauge is published from, so the console and the alert cannot "
            + "disagree about the same intent.",
            "The unprocessed backlog is the P6 inbox rows still holding a null ProcessedAt (an "
            + "Unknown dispute event, or a settlement for a purpose this phase does not apply). It is "
            + "provider-wide, not organisation-scoped, because a provider event carries no tenant.",
        };

        if (truncated)
        {
            notes.Add(
                $"Only the {MaxIntentsScanned} most recent intents in the window were inspected; the "
                + "report is truncated. Narrow the window or the organisation to see all of them.");
        }

        if (unavailableProviders.Count > 0)
        {
            notes.Add(
                "These adapters could not be resolved, so every intent under them is reported "
                + $"unreconciled: {string.Join(", ", unavailableProviders)}.");
        }

        if (refundComparisonUnsupported.Count > 0)
        {
            notes.Add(
                "These adapters do not expose a refund read, so refund divergence was not compared "
                + $"for them: {string.Join(", ", refundComparisonUnsupported)}.");
        }

        return new PaymentReconciliationReport(
            OrganizationId: query.OrganizationId,
            Provider: query.Provider,
            From: from,
            To: to,
            IntentsChecked: candidates.Count,
            Truncated: truncated,
            UnreconciledIntents: rows,
            UnreconciledByProvider: unreconciledByProvider,
            UnprocessedWebhookBacklog: backlog,
            UnprocessedBacklogTotal: backlogTotal,
            CheckedAt: now,
            Notes: notes);
    }

    /// <summary>
    /// Compares one intent with the adapter that issued its charge. Returns <c>null</c> only when the
    /// two sides genuinely agree.
    /// </summary>
    private async Task<UnreconciledIntentRow?> InspectAsync(
        PaymentIntent intent, IPaymentProvider provider, DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(intent.ProviderIntentId))
        {
            return Describe(intent, providerStatus: null, ReconciliationReasons.ProviderIntentMissing, now);
        }

        ProviderPaymentIntent? providerIntent;
        try
        {
            providerIntent = await provider.GetPaymentIntentAsync(intent.ProviderIntentId, cancellationToken);
        }
        catch (PaymentDomainException exception)
        {
            logger.LogWarning(
                exception,
                "Payment reconciliation could not read provider state for intent {IntentId}.",
                intent.Id);
            return Describe(intent, providerStatus: null, ReconciliationReasons.ProviderUnavailable, now);
        }

        if (providerIntent is null)
        {
            return Describe(intent, providerStatus: null, ReconciliationReasons.ProviderUnknownIntent, now);
        }

        // The expiry is derivable (P3) and the sweep makes it durable (P7); reconciliation must see
        // it in the interval before a sweep pass, so the rule is restated here rather than assumed.
        if (intent.Status is PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing
            && intent.ExpiresAt is { } expiry
            && expiry <= now)
        {
            return Describe(intent, providerIntent.Status, ReconciliationReasons.ExpiredUnsettled, now);
        }

        if (providerIntent.Status != intent.Status)
        {
            var reason =
                providerIntent.Status == PaymentProviderStatus.Succeeded
                && intent.Status is PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing
                    ? ReconciliationReasons.ProviderSettledUnconfirmed
                    : ReconciliationReasons.StatusDiverged;

            return Describe(intent, providerIntent.Status, reason, now);
        }

        if (providerIntent.Amount.AmountMinor != intent.AmountMinor)
        {
            return Describe(intent, providerIntent.Status, ReconciliationReasons.AmountDiverged, now);
        }

        if (!string.Equals(providerIntent.Amount.Currency, intent.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Describe(intent, providerIntent.Status, ReconciliationReasons.CurrencyDiverged, now);
        }

        if (provider is IPaymentRefundReader refundReader)
        {
            var refunds = await refundReader.ListRefundsAsync(intent.ProviderIntentId, cancellationToken);
            var refundedMinor = refunds.Sum(refund => refund.Amount.AmountMinor);
            var providerRefundedInFull = refundedMinor > 0 && refundedMinor >= intent.AmountMinor;

            if (providerRefundedInFull && intent.RefundedAt is null)
            {
                return Describe(intent, providerIntent.Status, ReconciliationReasons.RefundNotRecorded, now);
            }

            if (refundedMinor == 0 && intent.RefundedAt is not null)
            {
                return Describe(
                    intent, providerIntent.Status, ReconciliationReasons.RefundRecordedWithoutProvider, now);
            }
        }

        return null;
    }

    /// <summary>
    /// Counts the unprocessed inbox rows by provider and carries a few of their errors. The counts
    /// come from a grouped read (so a large stuck backlog is still counted exactly); only the reason
    /// samples are capped.
    /// </summary>
    private async Task<IReadOnlyList<UnprocessedWebhookBacklogRow>> ReadBacklogAsync(
        string? providerFilter, CancellationToken cancellationToken)
    {
        var unprocessed = db.PaymentProviderEvents
            .AsNoTracking()
            .Where(providerEvent => providerEvent.ProcessedAt == null)
            .Where(providerEvent => providerFilter == null || providerEvent.Provider == providerFilter);

        var counts = await unprocessed
            .GroupBy(providerEvent => providerEvent.Provider)
            .Select(group => new
            {
                Provider = group.Key,
                Count = group.LongCount(),
                Oldest = group.Min(providerEvent => (DateTime?)providerEvent.ReceivedAt),
            })
            .ToListAsync(cancellationToken);

        if (counts.Count == 0)
        {
            return [];
        }

        var reasons = await unprocessed
            .Where(providerEvent => providerEvent.ProcessingError != null)
            .Select(providerEvent => new { providerEvent.Provider, providerEvent.ProcessingError })
            .ToListAsync(cancellationToken);

        var reasonsByProvider = reasons
            .GroupBy(row => row.Provider, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => row.ProcessingError!)
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxBacklogReasons)
                    .ToArray(),
                StringComparer.Ordinal);

        return counts
            .OrderByDescending(row => row.Count)
            .Select(row => new UnprocessedWebhookBacklogRow(
                row.Provider,
                row.Count,
                row.Oldest,
                reasonsByProvider.GetValueOrDefault(row.Provider, [])))
            .ToArray();
    }

    private static UnreconciledIntentRow Describe(
        PaymentIntent intent, PaymentProviderStatus? providerStatus, string reason, DateTime now) =>
        new(
            intent.Id,
            intent.OrganizationId,
            intent.Provider,
            intent.Purpose.ToString(),
            intent.Status.ToString(),
            providerStatus?.ToString(),
            reason,
            intent.AmountMinor / 100m,
            intent.Currency,
            intent.CreatedAt,
            intent.ExpiresAt,
            intent.SettledAt,
            intent.RefundedAt);
}
