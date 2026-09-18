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
}
