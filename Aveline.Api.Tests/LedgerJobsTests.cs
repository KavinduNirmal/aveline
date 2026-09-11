using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Jobs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>Issue #197 — the ledger background jobs.</summary>
public class LedgerJobsTests
{
    private static (ServiceProvider Provider, AppDbContext Context) BuildProvider()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Jobs_{Guid.NewGuid()}")
            .Options);

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddScoped<IBlossomLedgerRepository, BlossomLedgerRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock());

        return (services.BuildServiceProvider(), context);
    }

    private static async Task<(Guid OrgId, Guid AccountId)> SeedOrgWithAccountAsync(
        AppDbContext context, decimal limit, decimal granted, decimal adjusted, decimal used, DateTime periodEnd)
    {
        var ownerId = Guid.CreateVersion7();
        var org = new Organization
        {
            Id = Guid.CreateVersion7(), Name = "Job Org", Slug = $"job-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);

        var account = new UsageAccount
        {
            OrganizationId = org.Id,
            PeriodStart = periodEnd.AddMonths(-1),
            PeriodEnd = periodEnd,
            MonthlyBlossomLimit = limit,
            BlossomGranted = granted,
            BlossomAdjusted = adjusted,
            BlossomUsed = used,
            BlossomRemaining = limit + granted - adjusted - used,
        };
        context.UsageAccounts.Add(account);
        await context.SaveChangesAsync();
        return (org.Id, account.Id);
    }

    [Fact]
    public async Task ExpiryJob_WritesExpiryEntryOnce()
    {
        var (provider, context) = BuildProvider();
        var (orgId, accountId) = await SeedOrgWithAccountAsync(
            context, 1000m, 500m, 0m, 0m, DateTime.UtcNow.AddDays(10));

        context.BlossomLedgerEntries.Add(new BlossomLedgerEntry
        {
            OrganizationId = orgId,
            UsageAccountId = accountId,
            EntryType = BlossomLedgerEntryType.TopUpGrant,
            BlossomDelta = 500m,
            BlossomBalanceAfter = 1500m,
            Reason = "Grant that has now expired.",
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
        });
        await context.SaveChangesAsync();

        var job = new BlossomExpiryJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IDistributedJobLock>(),
            NullLogger<BlossomExpiryJob>.Instance);

        Assert.Equal(1, await job.RunAsync(CancellationToken.None));
        Assert.Equal(0, await job.RunAsync(CancellationToken.None));

        context.ChangeTracker.Clear();
        var expiry = Assert.Single(await context.BlossomLedgerEntries
            .Where(e => e.EntryType == BlossomLedgerEntryType.Expiry)
            .ToListAsync());
        Assert.Equal(-500m, expiry.BlossomDelta);

        var account = await context.UsageAccounts.SingleAsync(a => a.Id == accountId);
        Assert.Equal(500m, account.BlossomAdjusted);
        Assert.Equal(1000m, account.BlossomRemaining);
    }

    [Fact]
    public async Task RolloverJob_ClosesAndOpensExactlyOnePeriod()
    {
        var (provider, context) = BuildProvider();
        var (orgId, accountId) = await SeedOrgWithAccountAsync(
            context, 150m, 0m, 0m, 0m, DateTime.UtcNow.AddMinutes(-1));

        var job = new BillingPeriodRolloverJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IDistributedJobLock>(),
            NullLogger<BillingPeriodRolloverJob>.Instance);

        Assert.Equal(1, await job.RunAsync(CancellationToken.None));
        Assert.Equal(0, await job.RunAsync(CancellationToken.None));

        context.ChangeTracker.Clear();
        var closed = await context.UsageAccounts.SingleAsync(a => a.Id == accountId);
        Assert.True(closed.IsClosed);
        Assert.NotNull(closed.ClosedAt);

        var accounts = await context.UsageAccounts.Where(a => a.OrganizationId == orgId).ToListAsync();
        Assert.Equal(2, accounts.Count);

        var allocation = Assert.Single(await context.BlossomLedgerEntries
            .Where(e => e.EntryType == BlossomLedgerEntryType.PeriodAllocation)
            .ToListAsync());
        Assert.Equal(150m, allocation.BlossomDelta);
    }

    [Fact]
    public async Task CleanupJob_DeletesOnlyExpiredRecords()
    {
        var (provider, context) = BuildProvider();
        var repository = new IdempotencyRepository(context);
        repository.Add(new IdempotencyRecord
        {
            OrganizationId = Guid.CreateVersion7(), IdempotencyKey = "fresh", Endpoint = "POST /x",
            HttpMethod = "POST", RequestHash = "h", ResponseStatus = 200, ResponseBodyJson = "{}",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1),
        });
        repository.Add(new IdempotencyRecord
        {
            OrganizationId = Guid.CreateVersion7(), IdempotencyKey = "stale", Endpoint = "POST /x",
            HttpMethod = "POST", RequestHash = "h", ResponseStatus = 200, ResponseBodyJson = "{}",
            CreatedAt = DateTime.UtcNow.AddDays(-2), ExpiresAt = DateTime.UtcNow.AddHours(-1),
        });
        await repository.SaveChangesAsync();

        var job = new IdempotencyRecordCleanupJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IDistributedJobLock>(),
            NullLogger<IdempotencyRecordCleanupJob>.Instance);

        Assert.Equal(1, await job.RunAsync(CancellationToken.None));
        Assert.Equal(1, await context.IdempotencyRecords.CountAsync());
    }
}
