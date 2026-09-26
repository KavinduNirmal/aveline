using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R4 (issue #345) — the statement's paging.
///
/// The delivered statement read the whole window with `pageSize: int.MaxValue` and then `Skip`ped
/// and `Take`ped **in memory**, so a busy account materialised every ledger entry plus every
/// consumption row on every request, and the pager could never reach past the first page in any
/// useful way. These tests fail against that implementation, which is why they exist: the one that
/// matters drains more rows than a single page can hold.
///
/// One store per test: the in-memory provider keys its store by name globally, and a shared name
/// lets one test's rows answer another's assertions.
/// </summary>
public class BlossomStatementPagingTests
{
    private sealed class Store : IDisposable
    {
        private readonly string _name = $"Stmt_{Guid.NewGuid()}";

        public AppDbContext Context() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: _name)
                .Options);

        public void Dispose() { }
    }

    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BlossomService CreateService(AppDbContext context, InMemoryEventBus? bus = null) =>
        new(
            new BlossomLedgerRepository(context),
            new UsageRepository(context),
            new EntitlementResolver(new EntitlementRepository(context)),
            bus ?? new InMemoryEventBus(),
            new AuditService(
                new AuditRepository(context), new AuditRedactor(),
                new HttpContextAccessor(), NullLogger<AuditService>.Instance),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:MaxAdjustmentBlossoms"] = "1000000",
                ["Billing:LowBalanceThresholdPercent"] = "20",
            }).Build(),
            NullLogger<BlossomService>.Instance);

    private static async Task<Guid> SeedOrganizationAsync(Store store)
    {
        await using var context = store.Context();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"stmt_{ownerId:N}", Email = "stmt@aveline.lk",
            FirstName = "S", LastName = "T", Username = $"stmt_{ownerId:N}",
            UserRole = Roles.Owner, OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Statement", Slug = $"stmt-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    /// <summary>
    /// Seeds the account so its projection matches the rows written, then interleaves ledger and
    /// usage rows across the same instants — the union boundary the paging has to get right.
    /// </summary>
    private static async Task SeedInterleavedAsync(Store store, Guid orgId, int count)
    {
        await using var context = store.Context();
        var account = new UsageAccount
        {
            OrganizationId = orgId,
            PeriodStart = From,
            PeriodEnd = To,
            MonthlyBlossomLimit = 100_000m,
            BlossomGranted = count * 10m,
            BlossomUsed = count * 3m,
            BlossomRemaining = 100_000m + (count * 10m) - (count * 3m),
            PlanTierSnapshot = PlanTier.Bloom,
            Status = UsageAccountStatus.Active,
        };
        context.UsageAccounts.Add(account);
        await context.SaveChangesAsync();

        for (var index = 0; index < count; index++)
        {
            // Deliberately the same instant for a ledger row and a consumption row, so ordering
            // cannot rely on the timestamp alone.
            var at = From.AddHours(index / 2 + 1);

            if (index % 2 == 0)
            {
                context.BlossomLedgerEntries.Add(new BlossomLedgerEntry
                {
                    OrganizationId = orgId,
                    UsageAccountId = account.Id,
                    EntryType = BlossomLedgerEntryType.AdminCredit,
                    BlossomDelta = 10m,
                    BlossomBalanceAfter = 0m,
                    Reason = $"Grant number {index} with a long enough reason.",
                    SourceKind = BlossomSourceKind.Admin,
                    CreatedAt = at,
                });
            }
            else
            {
                context.AiUsageRecords.Add(new AiUsageRecord
                {
                    OrganizationId = orgId,
                    RequestId = $"req-{index}",
                    WorkflowId = $"wf-{index}",
                    Provider = "openai",
                    Model = "gpt-4o",
                    InputTokens = 1200,
                    OutputTokens = 300,
                    BlossomUnits = 3m,
                    ActualCostUsd = 0.0125m,
                    CreatedAt = at,
                });
            }
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The test the phase exists for: 250 rows cannot fit in one page, so a service that pages in
    /// memory cannot satisfy it, and a service whose ordering is not total will lose or repeat rows.
    /// </summary>
    [Fact]
    public async Task TwoHundredAndFiftyRows_DrainAcrossPages_EachSeenExactlyOnce()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store);
        await SeedInterleavedAsync(store, orgId, 250);

        await using var context = store.Context();
        var service = CreateService(context);

        var seen = new List<Guid>();
        var page = 1;
        int? total = null;

        while (true)
        {
            var statement = await service.GetStatementAsync(
                orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
                minAmount: null, maxAmount: null, page: page, pageSize: 100, default);

            total ??= statement.Total;
            if (statement.Items.Count == 0) break;

            // Every page reports the same total, because it describes the window, not the page.
            Assert.Equal(total, statement.Total);
            seen.AddRange(statement.Items.Select(item => item.Id));
            page++;
            if (page > 10) break;
        }

        Assert.Equal(250, total);
        Assert.Equal(250, seen.Count);
        Assert.Equal(250, seen.Distinct().Count());
    }

    [Fact]
    public async Task APageReportsTheWindowTotal_NotThePageLength()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store);
        await SeedInterleavedAsync(store, orgId, 30);

        await using var context = store.Context();

        var statement = await CreateService(context)
            .GetStatementAsync(
                orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
                minAmount: null, maxAmount: null, page: 1, pageSize: 10, default);

        Assert.Equal(30, statement.Total);
        Assert.Equal(10, statement.Items.Count);
        Assert.Equal(1, statement.Page);
        Assert.Equal(10, statement.PageSize);
    }

    [Fact]
    public async Task AFilterThatExcludesEverything_IsAnEmptyPage_NotAnError()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store);
        await SeedInterleavedAsync(store, orgId, 10);

        await using var context = store.Context();

        var statement = await CreateService(context)
            .GetStatementAsync(
                orgId, From, To, kind: "Entitlement", entryType: BlossomLedgerEntryType.Expiry,
                sourceKind: null, query: null, minAmount: null, maxAmount: null,
                page: 1, pageSize: 50, default);

        Assert.Equal(0, statement.Total);
        Assert.Empty(statement.Items);
        Assert.Equal(1, statement.Page);
    }

    [Fact]
    public async Task OrderingIsNewestFirst_AndStableAcrossPages()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store);
        await SeedInterleavedAsync(store, orgId, 20);

        await using var context = store.Context();
        var service = CreateService(context);

        var first = await service.GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 10, default);
        var second = await service.GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 2, pageSize: 10, default);

        // Newest first, and the two pages do not overlap.
        Assert.True(first.Items[0].OccurredAt >= first.Items[^1].OccurredAt);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task TheKindFilter_SelectsEntitlementOrConsumption()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store);
        await SeedInterleavedAsync(store, orgId, 20);

        await using var context = store.Context();
        var service = CreateService(context);

        var entitlements = await service.GetStatementAsync(
            orgId, From, To, kind: "Entitlement", entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 100, default);
        var consumption = await service.GetStatementAsync(
            orgId, From, To, kind: "Consumption", entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 100, default);

        Assert.Equal(10, entitlements.Total);
        Assert.All(entitlements.Items, item => Assert.Equal("Entitlement", item.Kind));
        Assert.Equal(10, consumption.Total);
        Assert.All(consumption.Items, item => Assert.Equal("Consumption", item.Kind));
    }

    /// <summary>
    /// The opening balance is derived from the projection, which is the fact the statement has to
    /// state rather than imply. It must still be the balance *before the window*, not before the
    /// page — otherwise paging would move it.
    /// </summary>
    [Fact]
    public async Task TheOpeningBalance_DoesNotMoveWithThePage()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store);
        await SeedInterleavedAsync(store, orgId, 40);

        await using var context = store.Context();
        var service = CreateService(context);

        var first = await service.GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 10, default);
        var last = await service.GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 4, pageSize: 10, default);

        Assert.Equal(first.OpeningBalance, last.OpeningBalance);
        Assert.Equal(first.ClosingBalance, last.ClosingBalance);
        Assert.Equal(first.Reconciliation.Drift, last.Reconciliation.Drift);
    }

    /// <summary>
    /// The alerting contract. `LedgerDerivedBalance` and `ReconciliationDrift` are shared with
    /// `SystemMetricCollector`, which feeds the `blossom.ledger.drift` alert, so the statement must
    /// keep reporting exactly the formula it always did.
    /// </summary>
    [Fact]
    public async Task TheReconciliationFormula_IsUnchanged()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store);
        await SeedInterleavedAsync(store, orgId, 10);

        await using var context = store.Context();
        var account = await context.UsageAccounts.SingleAsync();
        var service = CreateService(context);

        var statement = await service.GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 50, default);

        var nonAllocation = await context.BlossomLedgerEntries
            .Where(entry => entry.UsageAccountId == account.Id
                && entry.EntryType != BlossomLedgerEntryType.PeriodAllocation)
            .SumAsync(entry => (decimal?)entry.BlossomDelta) ?? 0m;

        var expectedDerived = BlossomService.LedgerDerivedBalance(account, nonAllocation);
        var expectedDrift = BlossomService.ReconciliationDrift(account, nonAllocation);

        Assert.Equal(expectedDerived, statement.Reconciliation.LedgerDerivedBalance);
        Assert.Equal(expectedDrift, statement.Reconciliation.Drift);
        // Offsets cancel: granted 10/row against used 3/row over ten rows is a real, non-zero drift.
        Assert.False(statement.Reconciliation.IsConsistent);
    }
}
