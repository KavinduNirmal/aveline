using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 — the PostgreSQL-backed invariants the in-memory provider cannot exercise: the
/// <c>Amount &gt; 0</c> CHECK and the per-organization filtered unique dedup index, applied by a real
/// migration.
///
/// This is the test that proves the **migration** enforces the rules rather than the service
/// happening to. It needs a working Docker daemon and applies the real migrations, exactly as
/// <c>IncomeLedgerPostgresTests</c> does for the platform's own journal.
/// </summary>
public class BoutiqueSaleLedgerPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgId;
    private Guid _otherOrgId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();
        _orgId = await SeedOrganizationAsync(context, "Boutique Ledger A");
        _otherOrgId = await SeedOrganizationAsync(context, "Boutique Ledger B");
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static async Task<Guid> SeedOrganizationAsync(AppDbContext context, string name)
    {
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"bs_{ownerId:N}", Email = "bs@aveline.lk",
            FirstName = "B", LastName = "S", Username = $"bs_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = name, Slug = $"bs-pg-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private BoutiqueSaleEntry Entry(
        decimal amount,
        string sourceRef,
        Guid? organizationId = null,
        BoutiqueSaleEntryStatus status = BoutiqueSaleEntryStatus.Recorded) => new()
        {
            OrganizationId = organizationId ?? _orgId,
            Kind = BoutiqueSaleEntryKind.Sale,
            SourceKind = BoutiqueSaleSourceKind.CounterWalkIn,
            SourceRef = sourceRef,
            ChargeBasis = BoutiqueSaleChargeBasis.Verified,
            Status = status,
            Amount = amount,
            Reason = "A reason long enough to satisfy the rule.",
            OccurredAt = DateTime.UtcNow,
            RecordedByUserId = Guid.CreateVersion7(),
        };

    private static string Describe(Exception exception)
    {
        var current = exception;
        var text = string.Empty;
        while (current is not null)
        {
            text += current.Message + " | ";
            current = current.InnerException;
        }

        return text;
    }

    [Fact]
    public async Task TheMigrationAppliesAndTheTableExists()
    {
        await using var context = new AppDbContext(_options);

        var count = await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM \"BoutiqueSaleEntries\"")
            .SingleAsync();

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TheAmountCheckConstraint_RejectsANonPositiveAmount(decimal amount)
    {
        await using var context = new AppDbContext(_options);
        context.BoutiqueSaleEntries.Add(Entry(amount, $"constraint-{Guid.NewGuid():N}"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("CK_BoutiqueSaleEntries_AmountPositive", Describe(exception));
    }

    [Fact]
    public async Task ARefundRow_StoresAPositiveAmount()
    {
        // The sign lives in `Kind`, so the check constraint is satisfiable by a refund. This is the
        // assertion that stops a future reader adding a signed column.
        await using var context = new AppDbContext(_options);
        var refund = Entry(5000m, $"refund-{Guid.NewGuid():N}");
        refund.Kind = BoutiqueSaleEntryKind.Refund;
        refund.SourceKind = BoutiqueSaleSourceKind.Refund;
        context.BoutiqueSaleEntries.Add(refund);

        await context.SaveChangesAsync();

        Assert.Equal(5000m, refund.Amount);
        Assert.Equal(-1, BoutiqueSaleEntry.SignOf(refund.Kind));
    }

    [Fact]
    public async Task TheDedupIndex_RejectsASecondLiveEntryForTheSameIdentity()
    {
        var sourceRef = $"payment-{Guid.NewGuid():N}";
        await using (var first = new AppDbContext(_options))
        {
            first.BoutiqueSaleEntries.Add(Entry(1000m, sourceRef));
            await first.SaveChangesAsync();
        }

        await using var second = new AppDbContext(_options);
        second.BoutiqueSaleEntries.Add(Entry(2000m, sourceRef));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Contains("duplicate", Describe(exception), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheDedupIndex_IsScopedPerOrganization()
    {
        // Tenant isolation at the storage layer: two shops may legitimately hold the same provider
        // reference, and neither may block the other.
        var sourceRef = $"payment-{Guid.NewGuid():N}";
        await using var context = new AppDbContext(_options);
        context.BoutiqueSaleEntries.AddRange(
            Entry(1000m, sourceRef),
            Entry(2000m, sourceRef, organizationId: _otherOrgId));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.BoutiqueSaleEntries.CountAsync(e => e.SourceRef == sourceRef));
    }

    [Fact]
    public async Task TheDedupIndex_IsPartialOnANonNullSourceRef()
    {
        // A row without a reference has no identity to dedupe on, so two such rows must not collide.
        await using var context = new AppDbContext(_options);
        var first = Entry(1000m, "ignored");
        first.SourceRef = null;
        var second = Entry(1000m, "ignored");
        second.SourceRef = null;
        context.BoutiqueSaleEntries.AddRange(first, second);

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.BoutiqueSaleEntries.CountAsync(e => e.SourceRef == null));
    }

    /// <summary>
    /// Drives the **real service**, because two things it must do are only observable against a real
    /// database and neither is the ordering EF would choose:
    ///
    /// 1. EF sorts modification commands by entity type then by state, putting the INSERT ahead of
    ///    the UPDATE, so the void must be flushed first inside a transaction.
    /// 2. The filtered unique index does not look at <c>Status</c>, so a voided row whose
    ///    <c>SourceRef</c> is still populated keeps occupying the key and the takeover trips
    ///    <c>23505</c>. The void must therefore clear the reference as well.
    /// </summary>
    [Fact]
    public async Task TheService_SupersedesADerivedEntryWithAVerifiedReceipt()
    {
        var sourceRef = $"order-{Guid.NewGuid():N}";
        var actor = Guid.CreateVersion7();

        await using var context = new AppDbContext(_options);
        var service = new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance);

        var derived = await service.RecordAsync(new RecordBoutiqueSaleCommand(
            _orgId, 20000m, "Order reached a paid status; billed value.",
            BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Derived,
            BoutiqueSaleSourceKind.OrderSettlement, sourceRef,
            DateTime.UtcNow, actor));

        var verified = await service.RecordAsync(new RecordBoutiqueSaleCommand(
            _orgId, 20000m, "Payment confirmed against the same order.",
            BoutiqueSaleEntryKind.PaymentReceived, BoutiqueSaleChargeBasis.Verified,
            BoutiqueSaleSourceKind.OrderPayment, sourceRef,
            DateTime.UtcNow, actor, SupersedesEntryId: derived.Id));

        Assert.Equal(derived.Id, verified.SupersedesEntryId);

        var reloaded = await context.BoutiqueSaleEntries
            .AsNoTracking()
            .FirstAsync(entry => entry.Id == derived.Id);
        Assert.Equal(BoutiqueSaleEntryStatus.Voided, reloaded.Status);
        Assert.Null(reloaded.SourceRef);
    }
}
