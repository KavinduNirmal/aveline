using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #190 — the M3 backfill must leave <c>BlossomRemaining</c> numerically unchanged
/// and synthesise one <c>PeriodAllocation</c> entry per existing period (domain-model.md §10.1).
/// </summary>
public class BillingMigrationBackfillTests : IAsyncLifetime
{
    private const string PreM3Migration = "20260911162158_AddBlossomPricingRules";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");

        // Stop at M2 so the M3 backfill sees genuine pre-M3 rows.
        await _context.Database.GetService<IMigrator>().MigrateAsync(PreM3Migration);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task M3_Backfill_PreservesRemainingAndSynthesisesPeriodAllocation()
    {
        var userId = Guid.CreateVersion7();
        var orgId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var periodStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id","ClerkId","FirstName","LastName","OrganizationId","Email","Username",
              "PhoneNumber","UserRole","OrganizationRole","ContactPreference","CreatedAt","UpdatedAt")
            VALUES ({0}, {1}, 'Backfill', 'Owner', 'org_legacy', 'backfill@aveline.lk', {2},
              '+94770000000', 'owner', 'org:boutique_owner', 'Email', {3}, {3})
            """,
            userId, $"clerk_{userId:N}", $"backfill_{userId:N}", DateTime.UtcNow);

        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Organizations" ("Id","Name","Slug","OwnerUserId","CreatedAt","UpdatedAt","PlanTier")
            VALUES ({0}, 'Backfill Boutique', {1}, {2}, {3}, {3}, 'Bloom')
            """,
            orgId, $"backfill-{orgId:N}", userId, DateTime.UtcNow);

        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "UsageAccounts" ("Id","OrganizationId","PeriodStart","PeriodEnd","MonthlyBlossomLimit",
              "BlossomUsed","BlossomRemaining","ActiveCustomerCount","StaffCount","Status","UpdatedAt")
            VALUES ({0}, {1}, {2}, {3}, 750, 132.4, 617.6, 0, 0, 'Active', {4})
            """,
            accountId, orgId, periodStart, periodEnd, DateTime.UtcNow);

        // Apply M3 (and anything later).
        await _context.Database.MigrateAsync();
        _context.ChangeTracker.Clear();

        var account = await _context.UsageAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        Assert.Equal(617.6m, account.BlossomRemaining);
        Assert.Equal(0m, account.BlossomGranted);
        Assert.Equal(0m, account.BlossomAdjusted);
        Assert.Equal(PlanTier.Bloom, account.PlanTierSnapshot);

        var entries = await _context.BlossomLedgerEntries
            .AsNoTracking()
            .Where(e => e.UsageAccountId == accountId)
            .ToListAsync();

        var allocation = Assert.Single(entries);
        Assert.Equal(BlossomLedgerEntryType.PeriodAllocation, allocation.EntryType);
        Assert.Equal(750m, allocation.BlossomDelta);
        Assert.Equal(617.6m, allocation.BlossomBalanceAfter);
    }
}
