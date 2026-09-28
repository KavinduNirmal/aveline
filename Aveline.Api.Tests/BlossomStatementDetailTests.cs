using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R4 (issue #345) — what a statement row can explain.
///
/// The delivered statement rendered every consumption line as `"Agent workflow"` with a workflow id
/// and nothing else, so an operator asking *"where did 400 Blossoms go?"* had no answer the page
/// could give. These tests pin the fields that make a line explicable, and the flag that stops a
/// derived figure being mistaken for a measured one.
/// </summary>
public class BlossomStatementDetailTests
{
    private sealed class Store : IDisposable
    {
        private readonly string _name = $"StmtDetail_{Guid.NewGuid()}";

        public AppDbContext Context() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: _name)
                .Options);

        public void Dispose() { }
    }

    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BlossomService CreateService(AppDbContext context) =>
        new(
            new BlossomLedgerRepository(context),
            new UsageRepository(context),
            new EntitlementResolver(new EntitlementRepository(context)),
            new InMemoryEventBus(),
            new AuditService(
                new AuditRepository(context), new AuditRedactor(),
                new HttpContextAccessor(), NullLogger<AuditService>.Instance),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:MaxAdjustmentBlossoms"] = "1000000",
            }).Build(),
            NullLogger<BlossomService>.Instance);

    private static async Task<(Guid OrgId, Guid AccountId)> SeedAsync(Store store)
    {
        await using var context = store.Context();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"det_{ownerId:N}", Email = "det@aveline.lk",
            FirstName = "D", LastName = "T", Username = $"det_{ownerId:N}",
            UserRole = Roles.Owner, OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Detail", Slug = $"det-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var account = new UsageAccount
        {
            OrganizationId = org.Id,
            PeriodStart = From,
            PeriodEnd = To,
            MonthlyBlossomLimit = 10_000m,
            BlossomRemaining = 10_000m,
            PlanTierSnapshot = PlanTier.Bloom,
            Status = UsageAccountStatus.Active,
        };
        context.UsageAccounts.Add(account);
        await context.SaveChangesAsync();
        return (org.Id, account.Id);
    }

    private static async Task<Guid> SeedConsumptionAsync(Store store, Guid orgId, decimal units)
    {
        await using var context = store.Context();
        var record = new AiUsageRecord
        {
            OrganizationId = orgId,
            RequestId = "req-1",
            WorkflowId = "wf-1",
            Provider = "openai",
            Model = "gpt-4o",
            InputTokens = 1200,
            OutputTokens = 300,
            CachedTokens = 100,
            BlossomUnits = units,
            ActualCostUsd = 0.0125m,
            CreatedAt = From.AddHours(1),
        };
        context.AiUsageRecords.Add(record);
        await context.SaveChangesAsync();
        return record.Id;
    }

    private static async Task<Guid> SeedGrantAsync(
        Store store, Guid orgId, Guid accountId, decimal amount,
        BlossomLedgerEntryType type = BlossomLedgerEntryType.TopUpGrant,
        DateTime? expiresAt = null)
    {
        await using var context = store.Context();
        var entry = new BlossomLedgerEntry
        {
            OrganizationId = orgId,
            UsageAccountId = accountId,
            EntryType = type,
            BlossomDelta = amount,
            BlossomBalanceAfter = amount,
            Reason = "A grant with a long enough reason.",
            SourceKind = BlossomSourceKind.Admin,
            ExpiresAt = expiresAt,
            CreatedAt = From.AddHours(1),
        };
        context.BlossomLedgerEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    /// <summary>
    /// A consumption row carries what the charge was for: who served it, which model, how much raw
    /// usage, and what it cost. Without these a Blossom line is unexplainable.
    /// </summary>
    [Fact]
    public async Task AConsumptionRow_CarriesItsProviderModelUnitsAndCost()
    {
        using var store = new Store();
        var (orgId, _) = await SeedAsync(store);
        await SeedConsumptionAsync(store, orgId, 5m);

        await using var context = store.Context();
        var statement = await CreateService(context).GetStatementAsync(
            orgId, From, To, kind: "Consumption", entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 50, default);

        var item = Assert.Single(statement.Items);
        Assert.Equal("openai", item.Provider);
        Assert.Equal("gpt-4o", item.Model);
        // 1200 input + 300 output + 100 cached, so the Blossom charge can be checked against tokens.
        Assert.Equal(1600, item.NormalizedUnits);
        Assert.Equal(0.0125m, item.ActualCostUsd);
        Assert.Equal(-5m, item.BlossomDelta);
        // And the reason now names the model, rather than saying "Agent workflow".
        Assert.Contains("gpt-4o", item.Reason);
    }

    [Fact]
    public async Task ALedgerRow_CarriesNoneOfTheConsumptionFields()
    {
        using var store = new Store();
        var (orgId, accountId) = await SeedAsync(store);
        await SeedGrantAsync(store, orgId, accountId, 500m);

        await using var context = store.Context();
        var statement = await CreateService(context).GetStatementAsync(
            orgId, From, To, kind: "Entitlement", entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 50, default);

        var item = Assert.Single(statement.Items);
        // Nullable because a ledger row has none of them: an absent value must not be invented.
        Assert.Null(item.Provider);
        Assert.Null(item.Model);
        Assert.Null(item.NormalizedUnits);
        Assert.Null(item.ActualCostUsd);
    }

    /// <summary>
    /// The statement reports what a grant can still have revoked from it, so the redesign can offer
    /// an action instead of discovering non-revocability from a `409`. The 409 stays authoritative.
    /// </summary>
    [Fact]
    public async Task AGrantRow_ReportsWhatIsStillRevocable()
    {
        using var store = new Store();
        var (orgId, accountId) = await SeedAsync(store);
        var grantId = await SeedGrantAsync(store, orgId, accountId, 500m);

        await using (var context = store.Context())
        {
            context.BlossomLedgerEntries.Add(new BlossomLedgerEntry
            {
                OrganizationId = orgId,
                UsageAccountId = accountId,
                EntryType = BlossomLedgerEntryType.TopUpRevocation,
                BlossomDelta = -200m,
                BlossomBalanceAfter = 300m,
                Reason = "A partial revocation with a good reason.",
                SourceKind = BlossomSourceKind.Admin,
                SupersedesEntryId = grantId,
                CreatedAt = From.AddHours(2),
            });
            await context.SaveChangesAsync();
        }

        await using var read = store.Context();
        var statement = await CreateService(read).GetStatementAsync(
            orgId, From, To, kind: "Entitlement", entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 50, default);

        var grant = statement.Items.Single(item => item.Id == grantId);
        Assert.Equal(300m, grant.AvailableToRevoke);
    }

    /// <summary>
    /// A non-revocable row reports `null` rather than `0`: the field is "how much can be revoked",
    /// and a row that is not a revocable grant at all has no answer rather than an answer of zero.
    /// </summary>
    [Theory]
    [InlineData(BlossomLedgerEntryType.AdminDebit)]
    [InlineData(BlossomLedgerEntryType.PeriodAllocation)]
    public async Task ANonRevocableRow_ReportsNoRevocableAmount(BlossomLedgerEntryType type)
    {
        using var store = new Store();
        var (orgId, accountId) = await SeedAsync(store);
        var grantId = await SeedGrantAsync(store, orgId, accountId, 500m, type);

        await using var context = store.Context();
        var statement = await CreateService(context).GetStatementAsync(
            orgId, From, To, kind: "Entitlement", entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 50, default);

        var row = statement.Items.Single(item => item.Id == grantId);
        Assert.Null(row.AvailableToRevoke);
    }

    [Fact]
    public async Task AnExpiredGrant_ReportsNoRevocableAmount()
    {
        using var store = new Store();
        var (orgId, accountId) = await SeedAsync(store);
        var grantId = await SeedGrantAsync(
            store, orgId, accountId, 500m, expiresAt: DateTime.UtcNow.AddDays(-1));

        await using var context = store.Context();
        var statement = await CreateService(context).GetStatementAsync(
            orgId, From, To, kind: "Entitlement", entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 50, default);

        // `RevokeAsync` refuses an expired grant, so the statement must not advertise it.
        Assert.Null(statement.Items.Single(item => item.Id == grantId).AvailableToRevoke);
    }

    /// <summary>
    /// The statement says its opening balance came from the projection. It is derived backwards from
    /// the cached `BlossomRemaining`, not accumulated forward from the ledger, and a reader cannot
    /// tell the difference from the number alone.
    /// </summary>
    [Fact]
    public async Task TheStatement_StatesThatItsOpeningBalanceIsProjectionDerived()
    {
        using var store = new Store();
        var (orgId, _) = await SeedAsync(store);

        await using var context = store.Context();
        var statement = await CreateService(context).GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: null, maxAmount: null, page: 1, pageSize: 50, default);

        Assert.NotNull(statement.DataQuality);
        Assert.True(statement.DataQuality!.OpeningBalanceFromProjection);
        Assert.True(statement.DataQuality.ReconciliationChecked);
        Assert.False(statement.DataQuality.WindowCapped);
        Assert.Equal(BlossomService.MaxStatementWindowDays, statement.DataQuality.MaxWindowDays);
        Assert.Equal(BlossomService.MaxStatementWindowDays, statement.MaxWindowDays);
        Assert.NotEmpty(statement.DataQuality.Notes);
    }

    /// <summary>
    /// The filters narrow server-side, and `entryType` must not widen into consumption rows.
    /// </summary>
    [Fact]
    public async Task TheFilters_NarrowTheMergedSource()
    {
        using var store = new Store();
        var (orgId, accountId) = await SeedAsync(store);
        await SeedGrantAsync(store, orgId, accountId, 500m, BlossomLedgerEntryType.TopUpGrant);
        await SeedGrantAsync(store, orgId, accountId, -50m, BlossomLedgerEntryType.AdminDebit);
        await SeedConsumptionAsync(store, orgId, 5m);

        await using var context = store.Context();
        var service = CreateService(context);

        // By entry type: only the matching ledger row, and no consumption.
        var topUps = await service.GetStatementAsync(
            orgId, From, To, kind: null, entryType: BlossomLedgerEntryType.TopUpGrant,
            sourceKind: null, query: null, minAmount: null, maxAmount: null,
            page: 1, pageSize: 50, default);
        Assert.Equal(1, topUps.Total);
        Assert.Equal(BlossomLedgerEntryType.TopUpGrant, topUps.Items[0].EntryType);
        // `entryType` is a ledger concept, so the consumption row is not in this result even though
        // the `kind` filter was left open.
        Assert.All(topUps.Items, item => Assert.Equal("Entitlement", item.Kind));

        // By source kind.
        var bySource = await service.GetStatementAsync(
            orgId, From, To, kind: "Entitlement", entryType: null,
            sourceKind: BlossomSourceKind.Admin, query: null, minAmount: null, maxAmount: null,
            page: 1, pageSize: 50, default);
        Assert.Equal(2, bySource.Total);

        // By free text over the reason.
        var byQuery = await service.GetStatementAsync(
            orgId, From, To, kind: "Entitlement", entryType: null, sourceKind: null,
            query: "long enough reason", minAmount: null, maxAmount: null,
            page: 1, pageSize: 50, default);
        Assert.Equal(2, byQuery.Total);

        // By amount bounds, applied to the signed movement: the +500 grant is in range and the
        // -50 debit is not.
        var byAmount = await service.GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: 1m, maxAmount: null, page: 1, pageSize: 50, default);
        Assert.Equal(1, byAmount.Total);
        Assert.Equal(BlossomLedgerEntryType.TopUpGrant, byAmount.Items[0].EntryType);
    }

    /// <summary>
    /// An amount bound is a ledger concept — it bounds a *movement* — so a request that carries one
    /// returns no consumption rows at all. Letting them through would answer a question about
    /// movements with rows that have none, and the union's zero delta satisfies `delta >= 1`, so
    /// this is a real trap rather than a hypothetical one.
    /// </summary>
    [Fact]
    public async Task AnAmountBound_ExcludesConsumptionRowsEntirely()
    {
        using var store = new Store();
        var (orgId, accountId) = await SeedAsync(store);
        await SeedGrantAsync(store, orgId, accountId, 500m);
        await SeedConsumptionAsync(store, orgId, 5m);

        await using var context = store.Context();
        var statement = await CreateService(context).GetStatementAsync(
            orgId, From, To, kind: null, entryType: null, sourceKind: null, query: null,
            minAmount: 1m, maxAmount: null, page: 1, pageSize: 50, default);

        Assert.Equal(1, statement.Total);
        Assert.All(statement.Items, item => Assert.Equal("Entitlement", item.Kind));
    }
}
