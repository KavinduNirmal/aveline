using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
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

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var due = await db.UsageAccounts
            .Where(account => !account.IsClosed && account.PeriodEnd <= now)
            .ToListAsync(cancellationToken);

        var processed = 0;

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
                var tier = await db.Organizations
                    .Where(org => org.Id == account.OrganizationId)
                    .Select(org => (PlanTier?)org.PlanTier)
                    .FirstOrDefaultAsync(cancellationToken) ?? PlanTier.Seed;

                var limit = PlanEntitlementDefaults.For(tier).TryGetValue(UsageTrackerService.MonthlyBlossomsKey, out var entitlement)
                    ? entitlement.Number ?? 150m
                    : 150m;

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

        if (processed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return processed;
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
