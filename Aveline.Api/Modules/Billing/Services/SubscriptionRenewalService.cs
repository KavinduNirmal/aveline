using System.Globalization;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Creates the renewal charge at the period rollover and runs the dunning window (plan §9.4 F4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the receipt is a <see cref="IncomeSourceKind.System"/> row.</b> A system-created intent
/// has no human actor, and the income ledger refuses an unattributable money entry unless the
/// source kind says the write is a system one. Using <see cref="IncomeSourceKind.System"/> here is
/// the same rule <c>PaymentSettlementService.ReceiptSourceKind</c> applies to a system top-up. The
/// link back to the expectation is
/// <see cref="IncomeLedgerEntry.SupersedesEntryId"/>, which is what makes the
/// <c>Derived</c> and <c>Verified</c> rows reconcile to the same period rather than to two
/// identities.
/// </para>
/// <para>
/// <b>Why the schedule is anchored to the period boundary.</b> Dunning starts at
/// <c>CurrentPeriodEnd</c>, not at the instant the job happened to run, so a job that was down for
/// a day does not silently shift the day-1/3/7/14 policy.
/// </para>
/// </remarks>
public sealed class SubscriptionRenewalService(
    AppDbContext db,
    IPaymentIntentService intents,
    IIncomeLedgerService incomeLedger,
    INotificationDispatcher notifications,
    IAuditService auditService,
    PaymentMetrics metrics,
    ILogger<SubscriptionRenewalService> logger) : ISubscriptionRenewalService
{
    /// <summary>The audit action shape the payment module uses for a settled charge.</summary>
    private const string SettledAction = PaymentAuditActions.Settled;

    public async Task<int> RunAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var acted = 0;

        // The period boundary has passed and the subscription is still running: this is the
        // renewal charge, and it is the first attempt of the dunning window if it fails.
        var due = await db.OrganizationSubscriptions
            .Where(subscription =>
                subscription.Status == SubscriptionStatus.Active
                && subscription.PriceLkr > 0m
                && subscription.CurrentPeriodEnd <= now)
            .ToListAsync(cancellationToken);

        foreach (var subscription in due)
        {
            if (subscription.CancelAtPeriodEnd)
            {
                // Plan §9.5(b): the flag is honoured here. The subscription ends at the boundary it
                // was cancelled for, the closed period is not billed (the rollover's derived-charge
                // query excludes a scheduled cancellation), and nothing is renewed. The row is kept
                // rather than deleted, so the cancellation remains auditable.
                subscription.Status = SubscriptionStatus.Cancelled;
                subscription.NextRenewalAttemptAt = null;
                subscription.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Subscription cancelled at the period end. orgId={OrganizationId} "
                    + "periodEnd={PeriodEnd}",
                    subscription.OrganizationId, subscription.CurrentPeriodEnd);

                acted++;
                continue;
            }

            await AttemptAsync(subscription, now, cancellationToken);
            acted++;
        }

        var pastDue = await db.OrganizationSubscriptions
            .Where(subscription => subscription.Status == SubscriptionStatus.PastDue)
            .ToListAsync(cancellationToken);

        foreach (var subscription in pastDue)
        {
            var dunningStartedAt = subscription.DunningStartedAt ?? subscription.CurrentPeriodEnd;

            if (SubscriptionDunningSchedule.IsExpired(dunningStartedAt, now))
            {
                await ExpireAsync(subscription, now, cancellationToken);
                acted++;
                continue;
            }

            if (subscription.NextRenewalAttemptAt is not { } nextAttempt || nextAttempt > now)
            {
                continue;
            }

            await AttemptAsync(subscription, now, cancellationToken);
            acted++;
        }

        return acted;
    }

    /// <summary>One charge attempt, and its effect on the dunning state.</summary>
    private async Task<RenewalAttempt> AttemptAsync(
        OrganizationSubscription subscription, DateTime now, CancellationToken cancellationToken)
    {
        // The attempt index is what makes each retry a distinct charge at the provider while
        // keeping a re-run of the same attempt an idempotent replay.
        var attemptIndex = subscription.RenewalAttemptCount;

        PaymentIntentView? view;
        try
        {
            view = await intents.CreateAsync(
                new CreatePaymentIntentCommand(
                    subscription.OrganizationId,
                    PaymentPurpose.SubscriptionRenewal,
                    Money.Lkr(subscription.PriceLkr),
                    $"Plan renewal for {subscription.CurrentPeriodStart:yyyy-MM} "
                    + $"({subscription.PlanTier}).",
                    SkuCode: null,
                    BlossomQuantity: null,
                    IdempotencyKey: RenewalKey(subscription, attemptIndex),
                    CreatedByUserId: null,
                    BillingPeriodStart: subscription.CurrentPeriodStart,
                    BillingPeriodEnd: subscription.CurrentPeriodEnd,
                    PlanTier: subscription.PlanTier,
                    BillingCycle: subscription.BillingCycle.ToString()),
                cancellationToken);
        }
        catch (PaymentDomainException exception)
        {
            // A provider that cannot be reached, cannot be configured, or refuses a recurring
            // charge is a dunning failure, not a crash: the subscription is billed for the period
            // and the retry schedule takes over.
            logger.LogWarning(
                exception,
                "Renewal charge could not be created. orgId={OrganizationId} periodStart={PeriodStart}",
                subscription.OrganizationId, subscription.CurrentPeriodStart);
            return await MarkPastDueAsync(subscription, now, cancellationToken);
        }

        if (view.Status == nameof(PaymentProviderStatus.Succeeded))
        {
            var intent = await db.PaymentIntents
                .FirstAsync(row => row.Id == view.PaymentIntentId, cancellationToken);
            await ApplySettlementAsync(intent, now, cancellationToken);
            metrics.RecordSettlement(intent.Provider, nameof(PaymentPurpose.SubscriptionRenewal), "succeeded");

            logger.LogInformation(
                "Subscription renewal settled. orgId={OrganizationId} intentId={IntentId} "
                + "amountLkr={Amount} periodStart={PeriodStart}",
                subscription.OrganizationId, intent.Id, intent.PriceLkr, subscription.CurrentPeriodStart);

            return new RenewalAttempt(RenewalAttemptKind.Settled, intent.Id, "Settled");
        }

        metrics.RecordSettlement(view.Provider, nameof(PaymentPurpose.SubscriptionRenewal), "failed");

        logger.LogInformation(
            "Subscription renewal did not settle. orgId={OrganizationId} intentId={IntentId} "
            + "status={Status} attempt={Attempt}",
            subscription.OrganizationId, view.PaymentIntentId, view.Status, attemptIndex);

        return await MarkPastDueAsync(subscription, now, cancellationToken);
    }

    /// <summary>
    /// Moves the subscription to <c>PastDue</c> and schedules the next retry. The notification is
    /// raised on the transition only, so the day-1/3/7 retries do not each re-notify the owner.
    /// </summary>
    private async Task<RenewalAttempt> MarkPastDueAsync(
        OrganizationSubscription subscription, DateTime now, CancellationToken cancellationToken)
    {
        var firstFailure = subscription.Status != SubscriptionStatus.PastDue;

        subscription.Status = SubscriptionStatus.PastDue;
        subscription.DunningStartedAt ??= subscription.CurrentPeriodEnd;
        subscription.RenewalAttemptCount += 1;
        subscription.NextRenewalAttemptAt = SubscriptionDunningSchedule.NextAttemptAt(
            subscription.DunningStartedAt.Value, subscription.RenewalAttemptCount);
        subscription.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        if (firstFailure)
        {
            await NotifyAsync(
                subscription,
                NotificationType.SubscriptionPastDue,
                "Your subscription payment did not go through",
                "We could not collect this billing period's plan payment. We will retry, and your "
                + "plan will expire if payment is not received.",
                cancellationToken);
        }

        return new RenewalAttempt(
            RenewalAttemptKind.Failed, null, subscription.NextRenewalAttemptAt?.ToString("o"));
    }

    /// <summary>
    /// The dunning window closed without a settlement. Expiry is a state and a notification; it is
    /// deliberately not a deletion, and the read-only access rule is Phase 6's to enforce.
    /// </summary>
    private async Task ExpireAsync(
        OrganizationSubscription subscription, DateTime now, CancellationToken cancellationToken)
    {
        subscription.Status = SubscriptionStatus.Expired;
        subscription.NextRenewalAttemptAt = null;
        subscription.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(
            subscription,
            NotificationType.SubscriptionExpired,
            "Your subscription has expired",
            "Your subscription expired because payment could not be collected. Your data is safe "
            + "and nothing has been deleted.",
            cancellationToken);

        logger.LogInformation(
            "Subscription expired after the dunning window. orgId={OrganizationId} "
            + "dunningStartedAt={DunningStartedAt}",
            subscription.OrganizationId, subscription.DunningStartedAt);
    }

    public async Task<bool> ApplySettlementAsync(
        PaymentIntent intent, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var subscription = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(
                row => row.OrganizationId == intent.OrganizationId, cancellationToken);
        if (subscription is null)
        {
            logger.LogWarning(
                "A settled renewal has no subscription row. orgId={OrganizationId} intentId={IntentId}",
                intent.OrganizationId, intent.Id);
            return false;
        }

        var receiptRef = intent.ExternalRef ?? intent.ProviderIntentId;
        if (string.IsNullOrWhiteSpace(receiptRef))
        {
            return false;
        }

        // The receipt's own identity is the provider intent id, matching the system-top-up
        // convention, so a replay is detectable before the ledger's own dedup fires.
        var alreadyApplied = await db.IncomeLedgerEntries.AnyAsync(
            entry => entry.OrganizationId == intent.OrganizationId
                && entry.SourceKind == IncomeSourceKind.System
                && entry.SourceRef == receiptRef
                && entry.ChargeBasis == IncomeChargeBasis.Verified,
            cancellationToken);

        if (!alreadyApplied)
        {
            var periodStartIso = (intent.BillingPeriodStart ?? subscription.CurrentPeriodStart)
                .ToString("o", CultureInfo.InvariantCulture);

            // The Derived expectation the receipt takes over. It is in the billing source kind, so
            // the link is SupersedesEntryId rather than a shared identity.
            var derived = await db.IncomeLedgerEntries.FirstOrDefaultAsync(
                entry => entry.OrganizationId == intent.OrganizationId
                    && entry.SourceKind == IncomeSourceKind.SubscriptionBilling
                    && entry.SourceRef == periodStartIso
                    && entry.ChargeBasis == IncomeChargeBasis.Derived
                    && entry.Status == IncomeEntryStatus.Recorded,
                cancellationToken);

            await incomeLedger.RecordAsync(
                new RecordIncomeCommand(
                    intent.OrganizationId,
                    intent.PriceLkr,
                    $"Plan renewal for {subscription.CurrentPeriodStart:yyyy-MM} settled by "
                    + $"{intent.Provider}.",
                    IncomeEntryKind.SubscriptionCharge,
                    IncomeChargeBasis.Verified,
                    IncomeSourceKind.System,
                    receiptRef,
                    intent.BillingPeriodStart ?? subscription.CurrentPeriodStart,
                    intent.BillingPeriodEnd ?? subscription.CurrentPeriodEnd,
                    now,
                    RecordedByUserId: null,
                    SupersedesEntryId: derived?.Id),
                cancellationToken);
        }

        var closedEnd = intent.BillingPeriodEnd ?? subscription.CurrentPeriodEnd;
        if (subscription.CurrentPeriodStart < closedEnd)
        {
            subscription.CurrentPeriodStart = closedEnd;
            subscription.CurrentPeriodEnd = Advance(closedEnd, subscription.BillingCycle);
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.RenewalAttemptCount = 0;
        subscription.NextRenewalAttemptAt = null;
        subscription.DunningStartedAt = null;
        subscription.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        if (!alreadyApplied)
        {
            await auditService.RecordAsync(
                new AuditEntryRequest(
                    Action: SettledAction,
                    EntityType: nameof(PaymentIntent),
                    EntityId: intent.Id.ToString(),
                    OrganizationId: intent.OrganizationId,
                    ActorKind: AuditActorKind.System,
                    ActorUserId: null,
                    After: new
                    {
                        Status = nameof(PaymentProviderStatus.Succeeded),
                        SettledAt = now,
                        intent.PriceLkr,
                        PeriodStart = subscription.CurrentPeriodStart,
                    },
                    Reason: $"Provider {intent.Provider} settled subscription renewal "
                    + $"{receiptRef}."),
                cancellationToken);
        }

        return !alreadyApplied;
    }

    private static DateTime Advance(DateTime from, BillingCycle cycle) =>
        cycle == BillingCycle.Annual ? from.AddYears(1) : from.AddMonths(1);

    /// <summary>Deterministic per attempt, so a re-run of the same attempt replays the charge.</summary>
    private static string RenewalKey(OrganizationSubscription subscription, int attemptIndex) =>
        $"renewal:{subscription.OrganizationId:N}:"
        + $"{subscription.CurrentPeriodStart.ToString("o", CultureInfo.InvariantCulture)}:{attemptIndex}";

    private Task NotifyAsync(
        OrganizationSubscription subscription,
        NotificationType type,
        string title,
        string body,
        CancellationToken cancellationToken) =>
        notifications.DispatchAsync(
            new Notification(
                Type: type,
                Title: title,
                Body: body,
                Target: new NotificationTarget(
                    subscription.OrganizationId,
                    Roles: [Roles.BoutiqueOwner, Roles.Owner]),
                Data: new Dictionary<string, string?>
                {
                    ["planTier"] = subscription.PlanTier.ToString(),
                    ["amountLkr"] = subscription.PriceLkr.ToString(CultureInfo.InvariantCulture),
                    ["periodStart"] = subscription.CurrentPeriodStart.ToString("o"),
                },
                Channels: NotificationChannel.Realtime | NotificationChannel.Push),
            cancellationToken);
}
