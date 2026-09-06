using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUsageRepository"/>.
/// </summary>
public sealed class UsageRepository(AppDbContext db) : IUsageRepository
{
    /// <inheritdoc/>
    public async Task AddUsageRecordAndUpdateAccountAsync(
        AiUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        // The service has already calculated BlossomUnits; derive the current period
        // to locate the ledger row.
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd   = periodStart.AddMonths(1);

        var isRelational = db.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (isRelational)
        {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            // 1. Insert the immutable usage record.
            db.AiUsageRecords.Add(record);

        // 2. Get or create the billing period ledger row and increment the balance.
        var account = await db.UsageAccounts
            .FirstOrDefaultAsync(
                a => a.OrganizationId == record.OrganizationId
                  && a.PeriodStart    == periodStart,
                cancellationToken);

        if (account is null)
        {
            // Default limit — will be updated when subscription management is implemented.
            account = new UsageAccount
            {
                OrganizationId      = record.OrganizationId,
                PeriodStart         = periodStart,
                PeriodEnd           = periodEnd,
                MonthlyBlossomLimit = 150m, // Seed tier default
                BlossomUsed         = 0m,
                BlossomRemaining    = 150m,
            };
            db.UsageAccounts.Add(account);
        }

        account.BlossomUsed      += record.BlossomUnits;
        account.BlossomRemaining  = account.MonthlyBlossomLimit - account.BlossomUsed;
        account.UpdatedAt         = DateTime.UtcNow;

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

    /// <inheritdoc/>
    public async Task<UsageAccount> GetOrCreateAccountAsync(
        Guid organizationId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal defaultBlossomLimit,
        CancellationToken cancellationToken = default)
    {
        var account = await db.UsageAccounts
            .FirstOrDefaultAsync(
                a => a.OrganizationId == organizationId
                  && a.PeriodStart    == periodStart,
                cancellationToken);

        if (account is not null)
            return account;

        account = new UsageAccount
        {
            OrganizationId      = organizationId,
            PeriodStart         = periodStart,
            PeriodEnd           = periodEnd,
            MonthlyBlossomLimit = defaultBlossomLimit,
            BlossomUsed         = 0m,
            BlossomRemaining    = defaultBlossomLimit,
        };

        db.UsageAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AiUsageRecord>> GetUsageRecordsAsync(
        Guid organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return await db.AiUsageRecords
            .Where(r => r.OrganizationId == organizationId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
}
