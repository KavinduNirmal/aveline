using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Revenue.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Jobs;

/// <summary>
/// Base for the ledger jobs. Each run opens its own scope and takes the distributed job
/// lock so a multi-instance deployment does not run it twice (implementation-plan.md §5.2).
/// </summary>
public abstract class LedgerJobBase(
    IServiceScopeFactory scopeFactory,
    Common.Jobs.IDistributedJobLock jobLock,
    ILogger logger) : BackgroundService
{
    protected abstract string JobName { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>Performs one pass. Public so tests can run it deterministically.</summary>
    public abstract Task<int> RunAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunLockedAsync(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunLockedAsync(CancellationToken stoppingToken)
    {
        await using var handle = await jobLock.TryAcquireAsync(JobName, cancellationToken: stoppingToken);
        if (handle is null)
        {
            return;
        }

        try
        {
            var processed = await RunAsync(stoppingToken);
            logger.LogInformation(
                "Job {Job} completed. processed={Processed}", JobName, processed);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Job {Job} failed.", JobName);
        }
    }
}

/// <summary>
/// Writes an <c>Expiry</c> entry for every grant past its <c>ExpiresAt</c> that has not
/// already been fully revoked or expired (FR-2.9, BR-2.7).
/// </summary>
public sealed class BlossomExpiryJob(
    IServiceScopeFactory scopeFactory,
    Common.Jobs.IDistributedJobLock jobLock,
    ILogger<BlossomExpiryJob> logger)
    : LedgerJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "blossom-expiry";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<IBlossomLedgerRepository>();

        var now = DateTime.UtcNow;
        var expiring = await db.BlossomLedgerEntries
            .Where(entry => entry.ExpiresAt != null && entry.ExpiresAt <= now)
            .Where(entry => entry.BlossomDelta > 0)
            .ToListAsync(cancellationToken);

        var processed = 0;

        foreach (var grant in expiring)
        {
            var reversed = await db.BlossomLedgerEntries
                .Where(entry => entry.SupersedesEntryId == grant.Id)
                .Where(entry => entry.EntryType == BlossomLedgerEntryType.Expiry
                             || entry.EntryType == BlossomLedgerEntryType.TopUpRevocation)
                .SumAsync(entry => (decimal?)entry.BlossomDelta, cancellationToken);

            var alreadyReversed = reversed is null ? 0m : -reversed.Value;
            var amount = grant.BlossomDelta - alreadyReversed;
            if (amount <= 0)
            {
                continue;
            }

            var account = await db.UsageAccounts
                .FirstOrDefaultAsync(a => a.Id == grant.UsageAccountId, cancellationToken);
            if (account is null)
            {
                continue;
            }

            var entry = new BlossomLedgerEntry
            {
                OrganizationId = grant.OrganizationId,
                UsageAccountId = account.Id,
                EntryType = BlossomLedgerEntryType.Expiry,
                BlossomDelta = -amount,
                Reason = "Expiry of an unused Blossom grant.",
                SourceKind = BlossomSourceKind.Expiry,
                SupersedesEntryId = grant.Id,
                CreatedAt = now,
            };

            account.BlossomAdjusted += amount;
            account.BlossomRemaining =
                account.MonthlyBlossomLimit + account.BlossomGranted - account.BlossomAdjusted - account.BlossomUsed;
            account.UpdatedAt = now;

            await ledger.AddEntryAndUpdateAccountAsync(entry, account, cancellationToken);
            processed++;
        }

        return processed;
    }
}

/// <summary>
/// Closes every period whose end has passed and opens the next one with a fresh
/// <c>PeriodAllocation</c> (FR-2.5, BR-2.13).
/// </summary>
public sealed class BillingPeriodRolloverJob(
    IServiceScopeFactory scopeFactory,
    Common.Jobs.IDistributedJobLock jobLock,
    ILogger<BillingPeriodRolloverJob> logger)
    : LedgerJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "billing-period-rollover";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    /// <summary>Last-resort allowance when the resolver has no catalog entry at all.</summary>
    private const decimal SeedFallbackBlossomLimit = 150m;

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementResolver>();

        // The injected clock, so the dunning schedule (§9.4 F4) is testable without waiting days.
        var clock = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.UsageAccounts
            .Where(account => !account.IsClosed && account.PeriodEnd <= now)
            .ToListAsync(cancellationToken);

        var processed = 0;

        // A tenant can have several due accounts in one run; resolving its plan tier and
        // entitlement limit once per organization keeps this at O(distinct orgs) rather than
        // the 3N round-trips a per-account resolution cost (§3.8(e)).
        var tierByOrganization = new Dictionary<Guid, PlanTier>();
        var limitByOrganization = new Dictionary<Guid, decimal>();

        foreach (var account in due)
        {
            account.IsClosed = true;
            account.ClosedAt = now;
            account.UpdatedAt = now;

            var nextStart = account.PeriodEnd;
            var exists = await db.UsageAccounts.AnyAsync(
                a => a.OrganizationId == account.OrganizationId && a.PeriodStart == nextStart,
                cancellationToken);

            if (!exists)
            {
                if (!tierByOrganization.TryGetValue(account.OrganizationId, out var tier))
                {
                    tier = await db.Organizations
                        .Where(org => org.Id == account.OrganizationId)
                        .Select(org => (PlanTier?)org.PlanTier)
                        .FirstOrDefaultAsync(cancellationToken) ?? PlanTier.Seed;
                    tierByOrganization[account.OrganizationId] = tier;
                }

                // Resolve through IEntitlementResolver so a per-organisation override (or a
                // corrected catalog row) defines the next period's allowance (M-20).
                if (!limitByOrganization.TryGetValue(account.OrganizationId, out var limit))
                {
                    limit = await entitlements.GetDecimalAsync(
                        account.OrganizationId,
                        UsageTrackerService.MonthlyBlossomsKey,
                        SeedFallbackBlossomLimit,
                        at: null,
                        cancellationToken);
                    limitByOrganization[account.OrganizationId] = limit;
                }

                var next = new UsageAccount
                {
                    OrganizationId = account.OrganizationId,
                    PeriodStart = nextStart,
                    PeriodEnd = nextStart.AddMonths(1),
                    MonthlyBlossomLimit = limit,
                    PlanTierSnapshot = tier,
                    BlossomUsed = 0m,
                    BlossomGranted = 0m,
                    BlossomAdjusted = 0m,
                    BlossomRemaining = limit,
                    Status = UsageAccountStatus.Active,
                };
                db.UsageAccounts.Add(next);

                db.BlossomLedgerEntries.Add(new BlossomLedgerEntry
                {
                    OrganizationId = account.OrganizationId,
                    UsageAccountId = next.Id,
                    EntryType = BlossomLedgerEntryType.PeriodAllocation,
                    BlossomDelta = limit,
                    BlossomBalanceAfter = limit,
                    Reason = $"Plan allowance for {nextStart:yyyy-MM}.",
                    SourceKind = BlossomSourceKind.System,
                    CreatedAt = now,
                });
            }

            processed++;
        }

        // The revenue journal's derived charges ride the same unit of work as the allowance
        // rows, so a rollback cannot leave one without the other (Revenue Ledger R2, S-50).
        var derived = await WriteDerivedRevenueChargesAsync(db, now, cancellationToken);

        if (processed > 0 || derived > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Plan §9.4 F4. The renewing half is resolved from the scope rather than constructed, and
        // is optional on purpose: a host without the payment module still rolls periods over and
        // still writes the derived charges. The derived rows are committed first, because the
        // renewal receipt supersedes the derived charge for the same period.
        var renewals = scope.ServiceProvider.GetService<ISubscriptionRenewalService>();
        if (renewals is not null)
        {
            processed += await renewals.RunAsync(now, cancellationToken);
        }
        else
        {
            logger.LogDebug(
                "No subscription renewal service is registered; the rollover wrote derived charges "
                + "only.");
        }

        return processed;
    }

    /// <summary>
    /// Records what each period's list price says *should* be billed.
    /// </summary>
    /// <remarks>
    /// This writes <c>IncomeChargeBasis.Derived</c> and **never** <c>Verified</c>: a period
    /// boundary is not a receipt, and booking it as collected money would invent revenue. The
    /// <c>Verified</c> half is the renewal charge's — <see cref="ISubscriptionRenewalService"/>
    /// creates the intent and, when the provider settles it, writes the receipt that supersedes
    /// this row (plan §9.4 F4). <c>docs/api/README.md</c> §C.2 records the position.
    ///
    /// A subscription with `PriceLkr = 0` writes nothing at all. That was the production case
    /// before P1; a subscription the price resolver has not priced still reports
    /// `subscriptionPricesConfigured: false` with a `null` MRR rather than a zero that would read
    /// as free.
    ///
    /// The dedup reference is the period start in ISO-8601, so a re-run over the same period
    /// collides on the ledger's own identity instead of double-booking it.
    /// </remarks>
    private static async Task<int> WriteDerivedRevenueChargesAsync(
        AppDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var billable = await db.OrganizationSubscriptions
            .Where(subscription =>
                subscription.Status == SubscriptionStatus.Active
                && subscription.PriceLkr > 0m
                && subscription.CurrentPeriodEnd <= now
                // Plan §9.5(b): a subscription that scheduled cancellation at this boundary is not
                // renewed, and the period it is ending on is not billed. The status transition is
                // the renewal service's, and it runs after this write, so the exclusion is stated
                // here rather than relying on the status.
                && !subscription.CancelAtPeriodEnd)
            .Select(subscription => new
            {
                subscription.OrganizationId,
                subscription.PriceLkr,
                subscription.CurrentPeriodStart,
                subscription.CurrentPeriodEnd,
            })
            .ToListAsync(cancellationToken);

        if (billable.Count == 0)
        {
            return 0;
        }

        var organizationIds = billable.Select(row => row.OrganizationId).ToArray();
        var periodStarts = billable.Select(row => row.CurrentPeriodStart.ToString("o")).ToArray();

        // One read for the window's existing charges, so a re-run is a no-op rather than a
        // per-subscription round trip.
        var alreadyCharged = await db.IncomeLedgerEntries
            .Where(entry =>
                organizationIds.Contains(entry.OrganizationId)
                && entry.SourceKind == IncomeSourceKind.SubscriptionBilling
                && entry.Status == IncomeEntryStatus.Recorded
                && entry.SourceRef != null
                && periodStarts.Contains(entry.SourceRef))
            .Select(entry => new { entry.OrganizationId, entry.SourceRef })
            .ToListAsync(cancellationToken);

        var charged = alreadyCharged
            .Select(row => (row.OrganizationId, row.SourceRef))
            .ToHashSet();

        var written = 0;
        foreach (var subscription in billable)
        {
            var sourceRef = subscription.CurrentPeriodStart.ToString("o");
            if (!charged.Add((subscription.OrganizationId, sourceRef)))
            {
                continue;
            }

            db.IncomeLedgerEntries.Add(new IncomeLedgerEntry
            {
                OrganizationId = subscription.OrganizationId,
                Kind = IncomeEntryKind.SubscriptionCharge,
                SourceKind = IncomeSourceKind.SubscriptionBilling,
                SourceRef = sourceRef,
                ChargeBasis = IncomeChargeBasis.Derived,
                Status = IncomeEntryStatus.Recorded,
                Currency = "LKR",
                Amount = subscription.PriceLkr,
                Reason = $"Plan charge for {subscription.CurrentPeriodStart:yyyy-MM}.",
                PeriodStart = subscription.CurrentPeriodStart,
                PeriodEnd = subscription.CurrentPeriodEnd,
                OccurredAt = now,
                // A system write has no human actor; `null` is the established convention for that.
                RecordedByUserId = null,
            });
            written++;
        }

        return written;
    }
}

/// <summary>Deletes expired idempotency replay records (BR-2.9).</summary>
public sealed class IdempotencyRecordCleanupJob(
    IServiceScopeFactory scopeFactory,
    Common.Jobs.IDistributedJobLock jobLock,
    ILogger<IdempotencyRecordCleanupJob> logger)
    : LedgerJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "idempotency-record-cleanup";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>();
        return await repository.DeleteExpiredAsync(DateTime.UtcNow, cancellationToken);
    }
}

/// <summary>
/// Periodically calculates and materializes StaffCount and ActiveCustomerCount on active UsageAccount rows (T-4.12).
/// </summary>
public sealed class EntitlementCountingJob(
    IServiceScopeFactory scopeFactory,
    Common.Jobs.IDistributedJobLock jobLock,
    ILogger<EntitlementCountingJob> logger)
    : LedgerJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "entitlement-counts-materializer";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var accounts = await db.UsageAccounts
            .Where(a => !a.IsClosed && a.Status == UsageAccountStatus.Active)
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-90);
        var updated = 0;

        foreach (var account in accounts)
        {
            var staffCount = await db.OrganizationMemberships
                .CountAsync(m => m.OrganizationId == account.OrganizationId && m.Status == Organizations.Models.MembershipStatus.Active, cancellationToken);

            var activeCustomerCount = await db.CustomerInteractions
                .Where(i => i.OrganizationId == account.OrganizationId && i.CreatedAt >= cutoff)
                .Select(i => i.CustomerId)
                .Union(db.Orders.Where(o => o.OrganizationId == account.OrganizationId && o.CreatedAt >= cutoff).Select(o => o.CustomerId))
                .Union(db.Customers.Where(c => c.OrganizationId == account.OrganizationId && (c.UpdatedAt >= cutoff || c.CreatedAt >= cutoff)).Select(c => c.Id))
                .Distinct()
                .CountAsync(cancellationToken);

            account.StaffCount = staffCount;
            account.ActiveCustomerCount = activeCustomerCount;
            account.UpdatedAt = DateTime.UtcNow;
            updated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return updated;
    }
}
