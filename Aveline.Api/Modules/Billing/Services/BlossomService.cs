using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="IBlossomService"/>. Every mutation writes one append-only ledger
/// entry and updates the O(1) balance projection in the same transaction (FR-2.1,
/// BR-2.14), retrying on optimistic-concurrency conflicts up to five times.
/// </summary>
public sealed class BlossomService(
    IBlossomLedgerRepository ledgerRepository,
    IUsageRepository usageRepository,
    IEntitlementResolver entitlementResolver,
    IEventBus eventBus,
    IAuditService auditService,
    IConfiguration configuration,
    ILogger<BlossomService> logger) : IBlossomService
{
    private const int MaxRetryAttempts = 5;
    private const int MinReasonLength = 10;
    private const int MaxReasonLength = 500;

    /// <summary>Default page for a statement request that names no page size.</summary>
    public const int DefaultStatementPageSize = 50;

    /// <summary>
    /// Largest statement page. A request beyond it is rejected by the endpoint naming the range
    /// rather than silently clamped: a caller who asked for 500 rows and received 200 would page on
    /// a false assumption.
    /// </summary>
    public const int MaxStatementPageSize = 200;

    /// <summary>
    /// Longest statement window. Was 92 days at the endpoint, shorter than the 400-day retention the
    /// S-catalog claims for S-3; 400 now matches it.
    /// </summary>
    public const int MaxStatementWindowDays = 400;

    /// <summary>
    /// The balance implied by the append-only ledger:
    /// <c>MonthlyBlossomLimit + Σ non-allocation deltas − BlossomUsed</c>. Shared with the
    /// system metric collector so the alerting drift signal is derived with exactly the
    /// statement formula (BR-2.14, S-30).
    /// </summary>
    public static decimal LedgerDerivedBalance(UsageAccount account, decimal nonAllocationDeltas)
        => account.MonthlyBlossomLimit + nonAllocationDeltas - account.BlossomUsed;

    /// <summary>
    /// The signed difference between the cached projection (<see cref="UsageAccount.BlossomRemaining"/>)
    /// and <see cref="LedgerDerivedBalance"/>. A non-zero value is a reconciliation defect.
    /// </summary>
    public static decimal ReconciliationDrift(UsageAccount account, decimal nonAllocationDeltas)
        => account.BlossomRemaining - LedgerDerivedBalance(account, nonAllocationDeltas);

    public async Task<BlossomLedgerEntry> CreditAsync(
        CreditBlossomsCommand command, CancellationToken cancellationToken = default)
    {
        ValidateAmount(command.Amount);
        ValidateReason(command.Reason);
        ValidateCap(command.Amount);

        var account = await GetOrCreateAccountAsync(command.OrganizationId, cancellationToken);
        EnsureOpen(account);

        if (command.IdempotencyKey is not null && command.IdempotencyScope is not null)
        {
            var existing = await ledgerRepository.FindEntryByKeyAsync(
                command.OrganizationId, command.IdempotencyScope, command.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var now = DateTime.UtcNow;
        if (command.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            throw new BlossomValidationException("ExpiresAt must be in the future.");
        }

        var entry = new BlossomLedgerEntry
        {
            OrganizationId = command.OrganizationId,
            UsageAccountId = account.Id,
            EntryType = command.EntryType,
            BlossomDelta = command.Amount,
            Reason = command.Reason,
            SourceKind = command.SourceKind,
            SourceRef = command.SourceRef,
            ExpiresAt = command.ExpiresAt,
            IdempotencyKey = command.IdempotencyKey,
            IdempotencyScope = command.IdempotencyScope,
            CreatedByUserId = command.ActorUserId,
            CreatedAt = now,
        };

        var saved = await PersistAsync(entry, account, command.Amount, cancellationToken);
        await PublishBalanceEventsAsync(saved, account, cancellationToken);
        await RecordAuditAsync(saved, command.ActorUserId, cancellationToken);
        return saved;
    }

    public async Task<BlossomLedgerEntry> DebitAsync(
        DebitBlossomsCommand command, CancellationToken cancellationToken = default)
    {
        ValidateAmount(command.Amount);
        ValidateReason(command.Reason);
        ValidateCap(command.Amount);

        var account = await GetOrCreateAccountAsync(command.OrganizationId, cancellationToken);
        EnsureOpen(account);

        if (command.IdempotencyKey is not null && command.IdempotencyScope is not null)
        {
            var existing = await ledgerRepository.FindEntryByKeyAsync(
                command.OrganizationId, command.IdempotencyScope, command.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        if (!command.AllowNegative && command.Amount > account.BlossomRemaining)
        {
            throw new InsufficientBalanceException(account.BlossomRemaining, command.Amount);
        }

        var entry = new BlossomLedgerEntry
        {
            OrganizationId = command.OrganizationId,
            UsageAccountId = account.Id,
            EntryType = BlossomLedgerEntryType.AdminDebit,
            BlossomDelta = -command.Amount,
            Reason = command.Reason,
            SourceKind = BlossomSourceKind.Admin,
            IdempotencyKey = command.IdempotencyKey,
            IdempotencyScope = command.IdempotencyScope,
            CreatedByUserId = command.ActorUserId,
            CreatedAt = DateTime.UtcNow,
        };

        var saved = await PersistAsync(entry, account, -command.Amount, cancellationToken);
        await PublishBalanceEventsAsync(saved, account, cancellationToken);
        await RecordAuditAsync(saved, command.ActorUserId, cancellationToken);
        return saved;
    }

    public async Task<BlossomLedgerEntry> RevokeAsync(
        RevokeBlossomsCommand command, CancellationToken cancellationToken = default)
    {
        ValidateReason(command.Reason);

        var account = await GetOrCreateAccountAsync(command.OrganizationId, cancellationToken);
        EnsureOpen(account);

        if (command.IdempotencyKey is not null && command.IdempotencyScope is not null)
        {
            var existing = await ledgerRepository.FindEntryByKeyAsync(
                command.OrganizationId, command.IdempotencyScope, command.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var grant = await ledgerRepository.GetEntryAsync(command.LedgerEntryId, cancellationToken)
            ?? throw new BlossomLedgerEntryNotFoundException(command.LedgerEntryId);

        if (grant.OrganizationId != command.OrganizationId)
        {
            throw new BlossomLedgerEntryNotFoundException(command.LedgerEntryId);
        }

        var isRevocableType = grant.EntryType is BlossomLedgerEntryType.TopUpGrant
            or BlossomLedgerEntryType.AdminCredit
            or BlossomLedgerEntryType.PlanUpgradeProration;

        if (!isRevocableType || grant.BlossomDelta <= 0)
        {
            throw new GrantNotRevocableException(0m, "The referenced entry is not a revocable grant.");
        }

        if (grant.ExpiresAt is { } expiry && expiry <= DateTime.UtcNow)
        {
            throw new GrantNotRevocableException(0m, "The grant has already expired.");
        }

        var revoked = await ledgerRepository.GetRevokedAmountAsync(grant.Id, cancellationToken);
        var availableToRevoke = grant.BlossomDelta - revoked;
        if (availableToRevoke <= 0)
        {
            throw new GrantNotRevocableException(0m, "The grant has already been fully revoked.");
        }

        var entry = new BlossomLedgerEntry
        {
            OrganizationId = command.OrganizationId,
            UsageAccountId = account.Id,
            EntryType = BlossomLedgerEntryType.TopUpRevocation,
            BlossomDelta = -availableToRevoke,
            Reason = command.Reason,
            SourceKind = BlossomSourceKind.Admin,
            SupersedesEntryId = grant.Id,
            IdempotencyKey = command.IdempotencyKey,
            IdempotencyScope = command.IdempotencyScope,
            CreatedByUserId = command.ActorUserId,
            CreatedAt = DateTime.UtcNow,
        };

        var saved = await PersistAsync(entry, account, -availableToRevoke, cancellationToken);
        await PublishBalanceEventsAsync(saved, account, cancellationToken);
        await RecordAuditAsync(saved, command.ActorUserId, cancellationToken);
        return saved;
    }

    public async Task<BlossomBalance> GetBalanceAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var account = await GetOrCreateAccountAsync(organizationId, cancellationToken);
        return new BlossomBalance(
            account.OrganizationId,
            account.PeriodStart,
            account.PeriodEnd,
            account.IsClosed,
            account.PlanTierSnapshot,
            account.MonthlyBlossomLimit,
            account.BlossomGranted,
            account.BlossomAdjusted,
            account.BlossomUsed,
            account.BlossomRemaining,
            DateTime.UtcNow);
    }

    public async Task<BlossomLedgerEntry?> ApplyPlanChangeAsync(
        ApplyPlanChangeCommand command, CancellationToken cancellationToken = default)
    {
        if (command.BlossomDelta == 0)
        {
            return null;
        }

        ValidateReason(command.Reason);

        var account = await GetOrCreateAccountAsync(command.OrganizationId, cancellationToken);
        EnsureOpen(account);

        var entry = new BlossomLedgerEntry
        {
            OrganizationId = command.OrganizationId,
            UsageAccountId = account.Id,
            EntryType = command.EntryType,
            BlossomDelta = command.BlossomDelta,
            Reason = command.Reason,
            SourceKind = BlossomSourceKind.PlanChange,
            IdempotencyKey = command.IdempotencyKey,
            IdempotencyScope = command.IdempotencyScope,
            CreatedByUserId = command.ActorUserId,
            CreatedAt = DateTime.UtcNow,
        };

        var saved = await PersistAsync(entry, account, command.BlossomDelta, cancellationToken);
        await PublishBalanceEventsAsync(saved, account, cancellationToken);
        await RecordAuditAsync(saved, command.ActorUserId, cancellationToken);
        return saved;
    }

    public async Task<BlossomStatement> GetStatementAsync(
        Guid organizationId, DateTime from, DateTime to, string? kind,
        BlossomLedgerEntryType? entryType, BlossomSourceKind? sourceKind, string? query,
        decimal? minAmount, decimal? maxAmount, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new BlossomValidationException("The window end must be after its start.");
        }

        var account = await GetOrCreateAccountAsync(organizationId, cancellationToken);
        var normalizedKind = kind?.ToLowerInvariant();
        var includeEntitlements = normalizedKind is null or "all" or "entitlement";
        var includeConsumption = normalizedKind is null or "all" or "consumption";

        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize <= 0 ? DefaultStatementPageSize : pageSize, 1, MaxStatementPageSize);

        var filter = new BlossomStatementFilter(
            from, to, includeEntitlements, includeConsumption, entryType, sourceKind,
            string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
            minAmount, maxAmount);

        // Paged across the merged source **in the database**. The delivered version read the whole
        // window with `pageSize = int.MaxValue` and skipped in memory, which materialised every
        // ledger entry and every consumption row on every request.
        var (total, pageRows) = await ledgerRepository.ListStatementPageAsync(
            organizationId, filter, safePage, safePageSize, cancellationToken);

        // The opening balance is derived backwards from the projection, which is why the response
        // says so. It is the balance before the *window*, so it does not move with the page.
        var windowLedger = await ledgerRepository.SumDeltasInWindowAsync(
            organizationId, from, to, cancellationToken);
        var windowUsage = await usageRepository.SumBlossomUnitsInWindowAsync(
            organizationId, from, to, cancellationToken);
        var openingBalance = account.BlossomRemaining - windowLedger + windowUsage;

        var running = openingBalance;
        if (safePage > 1)
        {
            // Everything the filter matched before this page, so each row's running balance is
            // correct on every page rather than restarting at the window's opening.
            running += await ledgerRepository.SumStatementMovementBeforeAsync(
                organizationId, filter, (safePage - 1) * safePageSize, cancellationToken);
        }

        var revocable = await ledgerRepository.GetRevocableAmountsAsync(
            pageRows.Where(row => row.IsEntitlement).Select(row => row.Id).ToArray(), cancellationToken);

        var items = new List<BlossomStatementItem>(pageRows.Count);
        foreach (var row in pageRows)
        {
            if (row.IsEntitlement)
            {
                running += row.BlossomDelta;
                items.Add(new BlossomStatementItem(
                    row.Id, row.OccurredAt, "Entitlement", row.EntryType,
                    row.BlossomDelta, running, row.Reason, row.SourceKind,
                    row.SourceRef, row.ExpiresAt, row.CreatedByUserId)
                {
                    AvailableToRevoke = revocable.TryGetValue(row.Id, out var amount) ? amount : null,
                });
            }
            else
            {
                running -= row.BlossomUnits;
                items.Add(new BlossomStatementItem(
                    row.Id, row.OccurredAt, "Consumption", null, -row.BlossomUnits,
                    running, $"AI workflow on {row.Model ?? "an unknown model"}.", null,
                    row.SourceRef, null, null)
                {
                    Provider = row.Provider,
                    Model = row.Model,
                    NormalizedUnits = row.NormalizedUnits,
                    ActualCostUsd = row.ActualCostUsd,
                });
            }
        }

        var nonAllocationDeltas = await ledgerRepository.SumDeltasExcludingAsync(
            account.Id, BlossomLedgerEntryType.PeriodAllocation, cancellationToken);
        var ledgerDerivedBalance = LedgerDerivedBalance(account, nonAllocationDeltas);
        var drift = ReconciliationDrift(account, nonAllocationDeltas);

        return new BlossomStatement(
            organizationId,
            account.PeriodStart,
            account.PeriodEnd,
            openingBalance,
            items,
            total,
            safePage,
            safePageSize,
            account.BlossomRemaining,
            new BlossomStatementReconciliation(
                account.BlossomRemaining, ledgerDerivedBalance, drift, drift == 0m),
            DateTime.UtcNow,
            MaxStatementWindowDays,
            new BlossomStatementDataQuality(
                ReconciliationChecked: true,
                // The projection is the source, so the opening balance inherits its state. Stated
                // rather than implied, because a reader cannot tell otherwise.
                OpeningBalanceFromProjection: true,
                WindowCapped: (to - from).TotalDays > MaxStatementWindowDays,
                MaxWindowDays: MaxStatementWindowDays,
                Notes:
                [
                    "The opening balance is derived from the balance projection, not accumulated "
                    + "forward from the ledger rows in this window.",
                ]));
    }

    public async Task<BlossomUsage> GetUsageAsync(
        Guid organizationId, DateTime from, DateTime to, string groupBy,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new BlossomValidationException("The window end must be after its start.");
        }

        var records = await usageRepository.ListRecordsInWindowAsync(organizationId, from, to, cancellationToken);

        string KeySelector(AiUsageRecord record) => groupBy?.ToLowerInvariant() switch
        {
            "provider" => record.Provider,
            "model" => record.Model,
            "workflowid" or "workflow" => record.WorkflowId,
            _ => record.CreatedAt.ToString("yyyy-MM-dd"),
        };

        var series = records
            .GroupBy(KeySelector)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new BlossomUsagePoint(
                group.Key,
                group.Sum(record => record.BlossomUnits),
                group.Sum(record => (long)record.InputTokens + record.OutputTokens + record.CachedTokens),
                group.Count()))
            .ToArray();

        return new BlossomUsage(
            from,
            to,
            records.Sum(record => record.BlossomUnits),
            records.Sum(record => (long)record.InputTokens + record.OutputTokens + record.CachedTokens),
            series,
            DateTime.UtcNow);
    }

    private async Task<UsageAccount> GetOrCreateAccountAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty)
        {
            throw new BlossomValidationException("OrganizationId must not be empty.");
        }

        var (periodStart, periodEnd) = GetCurrentPeriod();
        var existing = await ledgerRepository.GetAccountAsync(organizationId, periodStart, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var limit = await entitlementResolver.GetDecimalAsync(
            organizationId, UsageTrackerService.MonthlyBlossomsKey, 150m, at: null, cancellationToken);

        return await usageRepository.GetOrCreateAccountAsync(
            organizationId, periodStart, periodEnd, limit, cancellationToken);
    }

    private async Task<BlossomLedgerEntry> PersistAsync(
        BlossomLedgerEntry entry, UsageAccount account, decimal delta, CancellationToken cancellationToken)
    {
        // Serialise mutations for the same period within this process so 20 concurrent
        // credits do not spend their retry budget fighting each other. Cross-instance
        // safety still comes from the xmin token and the retry loop below (BR-2.14).
        var gate = AccountLocks.GetOrAdd(account.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            ApplyDelta(account, delta);
            entry.BlossomBalanceAfter = account.BlossomRemaining;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await ledgerRepository.AddEntryAndUpdateAccountAsync(entry, account, cancellationToken);
                    return entry;
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxRetryAttempts)
                {
                    logger.LogWarning(
                        "Concurrent Blossom modification for org={OrganizationId}; retrying attempt {Attempt}.",
                        entry.OrganizationId, attempt);

                    // Re-read the freshest projection and re-apply the delta on top of it.
                    var refreshed = await ledgerRepository.ReloadAccountAsync(account, cancellationToken);
                    if (refreshed is not null)
                    {
                        account = refreshed;
                        ApplyDelta(account, delta);
                        entry.BlossomBalanceAfter = account.BlossomRemaining;
                    }

                    await Task.Delay(Random.Shared.Next(5, 25) * attempt, cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new ConcurrentModificationException();
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> AccountLocks = new();

    private static void ApplyDelta(UsageAccount account, decimal delta)
    {
        if (delta > 0)
        {
            account.BlossomGranted += delta;
        }
        else
        {
            account.BlossomAdjusted += -delta;
        }

        account.BlossomRemaining =
            account.MonthlyBlossomLimit + account.BlossomGranted - account.BlossomAdjusted - account.BlossomUsed;
        account.UpdatedAt = DateTime.UtcNow;
    }

    private async Task PublishBalanceEventsAsync(
        BlossomLedgerEntry entry, UsageAccount account, CancellationToken cancellationToken)
    {
        var eventType = entry.BlossomDelta > 0 ? "blossom.ledger.credited" : "blossom.ledger.debited";
        await eventBus.PublishAsync(
            eventType,
            entry.OrganizationId,
            new
            {
                ledgerEntryId = entry.Id,
                entryType = entry.EntryType.ToString(),
                blossomDelta = entry.BlossomDelta,
                balanceAfter = entry.BlossomBalanceAfter,
            },
            cancellationToken: cancellationToken);

        var thresholdPercent = configuration.GetValue("Billing:LowBalanceThresholdPercent", 20m);
        var threshold = account.MonthlyBlossomLimit * thresholdPercent / 100m;

        if (account.BlossomRemaining <= 0)
        {
            await eventBus.PublishAsync(
                "blossom.balance.exhausted", entry.OrganizationId,
                new { balance = account.BlossomRemaining }, cancellationToken: cancellationToken);
        }
        else if (account.BlossomRemaining <= threshold)
        {
            await eventBus.PublishAsync(
                "blossom.balance.threshold", entry.OrganizationId,
                new { balance = account.BlossomRemaining, thresholdPercent },
                cancellationToken: cancellationToken);
        }
    }

    private Task RecordAuditAsync(
        BlossomLedgerEntry entry, Guid? actorUserId, CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEntryRequest(
            Action: $"blossom.ledger.{entry.EntryType.ToString().ToLowerInvariant()}",
            EntityType: nameof(BlossomLedgerEntry),
            EntityId: entry.Id.ToString(),
            OrganizationId: entry.OrganizationId,
            ActorKind: actorUserId is null ? AuditActorKind.System : AuditActorKind.User,
            ActorUserId: actorUserId,
            After: new { entry.EntryType, entry.BlossomDelta, entry.BlossomBalanceAfter },
            Reason: entry.Reason), cancellationToken);

    private static void EnsureOpen(UsageAccount account)
    {
        if (account.IsClosed)
        {
            throw new PeriodClosedException();
        }
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
        {
            throw new BlossomValidationException("Amount must be greater than zero.");
        }

        if (decimal.Round(amount, 4) != amount)
        {
            throw new BlossomValidationException("Amount must not have more than four decimal places.");
        }
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)
            || reason.Length < MinReasonLength
            || reason.Length > MaxReasonLength)
        {
            throw new BlossomValidationException(
                $"Reason must be between {MinReasonLength} and {MaxReasonLength} characters.");
        }
    }

    private void ValidateCap(decimal amount)
    {
        var cap = configuration.GetValue("Billing:MaxAdjustmentBlossoms", 10000m);
        if (amount > cap)
        {
            throw new BlossomValidationException(
                $"Amount exceeds the configured maximum adjustment of {cap} Blossoms.");
        }
    }

    private static (DateTime PeriodStart, DateTime PeriodEnd) GetCurrentPeriod()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }
}
