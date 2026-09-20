using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>EF Core implementation of the append-only Blossom ledger.</summary>
public sealed class BlossomLedgerRepository(AppDbContext db) : IBlossomLedgerRepository
{
    public Task<UsageAccount?> GetAccountAsync(
        Guid organizationId, DateTime periodStart, CancellationToken cancellationToken = default) =>
        db.UsageAccounts.FirstOrDefaultAsync(
            account => account.OrganizationId == organizationId && account.PeriodStart == periodStart,
            cancellationToken);

    public async Task<UsageAccount?> ReloadAccountAsync(
        UsageAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Entry(account).ReloadAsync(cancellationToken);
            return account;
        }
        catch (InvalidOperationException)
        {
            // The row no longer exists.
            return null;
        }
    }

    public Task<BlossomLedgerEntry?> FindEntryByKeyAsync(
        Guid organizationId, string idempotencyScope, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        db.BlossomLedgerEntries.FirstOrDefaultAsync(
            entry => entry.OrganizationId == organizationId
                  && entry.IdempotencyScope == idempotencyScope
                  && entry.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public Task<BlossomLedgerEntry?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken = default) =>
        db.BlossomLedgerEntries.FirstOrDefaultAsync(entry => entry.Id == entryId, cancellationToken);

    public async Task AddEntryAndUpdateAccountAsync(
        BlossomLedgerEntry entry, UsageAccount account, CancellationToken cancellationToken = default)
    {
        var isRelational = db.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (isRelational)
        {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            db.BlossomLedgerEntries.Add(entry);
            db.UsageAccounts.Update(account);
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<decimal> GetRevokedAmountAsync(
        Guid grantEntryId, CancellationToken cancellationToken = default)
    {
        var revoked = await db.BlossomLedgerEntries
            .Where(entry => entry.SupersedesEntryId == grantEntryId)
            .Where(entry => entry.EntryType == BlossomLedgerEntryType.TopUpRevocation)
            .SumAsync(entry => (decimal?)entry.BlossomDelta, cancellationToken);

        return revoked is null ? 0m : -revoked.Value;
    }

    public async Task<IReadOnlyList<BlossomLedgerEntry>> ListEntriesAsync(
        Guid organizationId, DateTime? from, DateTime? to, BlossomLedgerEntryType? entryType,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await ApplyFilter(organizationId, from, to, entryType)
            .OrderByDescending(entry => entry.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountEntriesAsync(
        Guid organizationId, DateTime? from, DateTime? to, BlossomLedgerEntryType? entryType,
        CancellationToken cancellationToken = default) =>
        ApplyFilter(organizationId, from, to, entryType).CountAsync(cancellationToken);

    public async Task<decimal> SumDeltasAsync(Guid usageAccountId, CancellationToken cancellationToken = default)
    {
        var sum = await db.BlossomLedgerEntries
            .Where(entry => entry.UsageAccountId == usageAccountId)
            .SumAsync(entry => (decimal?)entry.BlossomDelta, cancellationToken);

        return sum ?? 0m;
    }

    public async Task<decimal> SumDeltasExcludingAsync(
        Guid usageAccountId, BlossomLedgerEntryType excludedType,
        CancellationToken cancellationToken = default)
    {
        var sum = await db.BlossomLedgerEntries
            .Where(entry => entry.UsageAccountId == usageAccountId)
            .Where(entry => entry.EntryType != excludedType)
            .SumAsync(entry => (decimal?)entry.BlossomDelta, cancellationToken);

        return sum ?? 0m;
    }

    private IQueryable<BlossomLedgerEntry> ApplyFilter(
        Guid organizationId, DateTime? from, DateTime? to, BlossomLedgerEntryType? entryType)
    {
        var query = db.BlossomLedgerEntries.Where(entry => entry.OrganizationId == organizationId);

        if (from is { } start)
        {
            query = query.Where(entry => entry.CreatedAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(entry => entry.CreatedAt < end);
        }

        if (entryType is { } type)
        {
            query = query.Where(entry => entry.EntryType == type);
        }

        return query;
    }

    /// <summary>
    /// The merged, filtered, ordered, paged statement. Both sources are narrowed in SQL and then
    /// unioned by their common columns, so the database does the ordering and the skip — which is
    /// the whole point of the rewrite: the delivered version materialised every row in the window
    /// and paged the list in memory.
    /// </summary>
    public async Task<(int Total, IReadOnlyList<BlossomStatementRow> Rows)> ListStatementPageAsync(
        Guid organizationId, BlossomStatementFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var merged = BuildStatementQuery(organizationId, filter);

        var total = await merged.CountAsync(cancellationToken);
        var ids = await merged
            // `Id` is the tie-break. UUIDv7 is monotonic, so the ordering is total and stable and a
            // row can be neither repeated nor skipped across pages.
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new { row.Id, row.IsEntitlement })
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return (total, []);
        }

        var ledgerIds = ids.Where(row => row.IsEntitlement).Select(row => row.Id).ToArray();
        var usageIds = ids.Where(row => !row.IsEntitlement).Select(row => row.Id).ToArray();

        var entries = await db.BlossomLedgerEntries
            .Where(entry => ledgerIds.Contains(entry.Id))
            .ToDictionaryAsync(entry => entry.Id, cancellationToken);
        var records = await db.AiUsageRecords
            .Where(record => usageIds.Contains(record.Id))
            .ToDictionaryAsync(record => record.Id, cancellationToken);

        var rows = new List<BlossomStatementRow>(ids.Count);
        foreach (var id in ids)
        {
            if (id.IsEntitlement && entries.TryGetValue(id.Id, out var entry))
            {
                rows.Add(new BlossomStatementRow(
                    entry.Id, entry.CreatedAt, true, entry.BlossomDelta, entry.EntryType,
                    entry.Reason, entry.SourceKind, entry.SourceRef, entry.ExpiresAt,
                    entry.CreatedByUserId, 0m, null, null, null, null));
            }
            else if (!id.IsEntitlement && records.TryGetValue(id.Id, out var record))
            {
                rows.Add(new BlossomStatementRow(
                    record.Id, record.CreatedAt, false, 0m, null, "AI workflow", null,
                    record.WorkflowId, null, null, record.BlossomUnits, record.Provider,
                    record.Model, NormalizedUnitsOf(record), record.ActualCostUsd));
            }
        }

        return (total, rows);
    }

    public async Task<decimal> SumDeltasInWindowAsync(
        Guid organizationId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var sum = await db.BlossomLedgerEntries
            .Where(entry => entry.OrganizationId == organizationId)
            .Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to)
            .SumAsync(entry => (decimal?)entry.BlossomDelta, cancellationToken);

        return sum ?? 0m;
    }

    public async Task<decimal> SumStatementMovementBeforeAsync(
        Guid organizationId, BlossomStatementFilter filter, int rowsToSkip,
        CancellationToken cancellationToken = default)
    {
        if (rowsToSkip <= 0)
        {
            return 0m;
        }

        // The balance movement of everything the filter matched *before* this page. Ledger deltas
        // are already signed; consumption reduces the balance, so it enters negated.
        var movement = await BuildStatementQuery(organizationId, filter)
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.Id)
            .Skip(rowsToSkip)
            .SumAsync(row => row.BlossomDelta - row.BlossomUnits, cancellationToken);

        return movement;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetRevocableAmountsAsync(
        IReadOnlyCollection<Guid> grantEntryIds, CancellationToken cancellationToken = default)
    {
        if (grantEntryIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var now = DateTime.UtcNow;

        // The revocable types, straight from `RevokeAsync`: a grant must be the right type, carry a
        // positive delta, and not have expired.
        var grants = await db.BlossomLedgerEntries
            .Where(entry => grantEntryIds.Contains(entry.Id))
            .Where(entry => entry.EntryType == BlossomLedgerEntryType.TopUpGrant
                || entry.EntryType == BlossomLedgerEntryType.AdminCredit
                || entry.EntryType == BlossomLedgerEntryType.PlanUpgradeProration)
            .Where(entry => entry.BlossomDelta > 0)
            .Where(entry => entry.ExpiresAt == null || entry.ExpiresAt > now)
            .Select(entry => new { entry.Id, entry.BlossomDelta })
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var ids = grants.Select(grant => grant.Id).ToArray();

        // One grouped read rather than one per grant: the statement can carry a whole page of them.
        var revokedByGrant = await db.BlossomLedgerEntries
            .Where(entry => entry.SupersedesEntryId != null
                && ids.Contains(entry.SupersedesEntryId!.Value)
                && entry.EntryType == BlossomLedgerEntryType.TopUpRevocation)
            .GroupBy(entry => entry.SupersedesEntryId!.Value)
            .Select(group => new { GrantId = group.Key, Revoked = group.Sum(entry => entry.BlossomDelta) })
            .ToListAsync(cancellationToken);

        var revoked = revokedByGrant.ToDictionary(row => row.GrantId, row => -row.Revoked);

        var revocable = new Dictionary<Guid, decimal>();
        foreach (var grant in grants)
        {
            var available = grant.BlossomDelta - revoked.GetValueOrDefault(grant.Id);
            if (available > 0)
            {
                revocable[grant.Id] = available;
            }
        }

        return revocable;
    }

    /// <summary>
    /// The union of both statement sources as one ordered shape. Both sides are narrowed in SQL
    /// before the union, so an `entryType` filter cannot widen into consumption rows.
    /// </summary>
    private IQueryable<BlossomStatementProjection> BuildStatementQuery(
        Guid organizationId, BlossomStatementFilter filter)
    {
        var ledger = db.BlossomLedgerEntries.Where(entry => entry.OrganizationId == organizationId);

        if (filter.EntryType is { } entryType)
        {
            ledger = ledger.Where(entry => entry.EntryType == entryType);
        }

        if (filter.SourceKind is { } sourceKind)
        {
            ledger = ledger.Where(entry => entry.SourceKind == sourceKind);
        }

        if (filter.Query is { } query)
        {
            ledger = ledger.Where(entry =>
                entry.Reason.Contains(query)
                || (entry.SourceRef != null && entry.SourceRef.Contains(query)));
        }

        if (filter.MinAmount is { } min)
        {
            ledger = ledger.Where(entry => entry.BlossomDelta >= min);
        }

        if (filter.MaxAmount is { } max)
        {
            ledger = ledger.Where(entry => entry.BlossomDelta <= max);
        }

        var ledgerRows = ledger.Select(entry => new BlossomStatementProjection
        {
            Id = entry.Id,
            OccurredAt = entry.CreatedAt,
            IsEntitlement = true,
            BlossomDelta = entry.BlossomDelta,
            BlossomUnits = 0m,
        });

        // A kind filter selects the union's legs outright, so "entitlement only" cannot widen into
        // consumption rows and vice versa.
        if (!filter.IncludeConsumption)
        {
            return ledgerRows;
        }

        // `entryType` and an amount bound are both **ledger** concepts. A consumption row has no
        // entry type and no delta at all (its effect is carried in `BlossomUnits`), so a request that
        // carries either must not answer with consumption. Both cases were real bugs before this
        // guard: the consumption leg ignored `entryType` entirely, and its zero delta satisfied
        // `delta >= 1`, so each returned consumption under a request that asked for ledger rows.
        if (filter.EntryType is not null || filter.MinAmount is not null || filter.MaxAmount is not null)
        {
            return ledgerRows;
        }

        var usage = db.AiUsageRecords.Where(record => record.OrganizationId == organizationId);

        if (filter.Query is { } usageQuery)
        {
            usage = usage.Where(record =>
                record.WorkflowId.Contains(usageQuery)
                || record.Provider.Contains(usageQuery)
                || record.Model.Contains(usageQuery));
        }

        var usageRows = usage.Select(record => new BlossomStatementProjection
        {
            Id = record.Id,
            OccurredAt = record.CreatedAt,
            IsEntitlement = false,
            BlossomDelta = 0m,
            BlossomUnits = record.BlossomUnits,
        });

        return filter.IncludeEntitlements ? ledgerRows.Union(usageRows) : usageRows;
    }

    /// <summary>
    /// The normalized units behind a usage row: the tokens it consumed, so a Blossom charge can be
    /// explained rather than taken on trust.
    /// </summary>
    private static long NormalizedUnitsOf(AiUsageRecord record) =>
        (long)record.InputTokens + record.OutputTokens + record.CachedTokens;
}
