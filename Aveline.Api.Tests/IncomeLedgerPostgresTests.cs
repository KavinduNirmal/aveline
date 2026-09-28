using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R1 (issue #342) — the PostgreSQL-backed invariants the in-memory provider cannot
/// exercise: the <c>Amount &gt; 0</c> CHECK and the filtered unique dedup index applied by a real
/// migration.
///
/// This is the test that proves the migration is what enforces the rules, rather than the service
/// happening to. It needs a working Docker daemon and applies the real migrations, exactly as
/// <c>LedgerPostgresTests</c> does.
/// </summary>
public class IncomeLedgerPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgId;

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
        _orgId = await SeedOrganizationAsync(context);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static async Task<Guid> SeedOrganizationAsync(AppDbContext context)
    {
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"rev_{ownerId:N}", Email = "rev@aveline.lk",
            FirstName = "Rev", LastName = "Owner", Username = $"rev_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Revenue Pg Boutique", Slug = $"rev-pg-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private IncomeLedgerEntry Entry(
        decimal amount,
        string sourceRef,
        IncomeEntryStatus status = IncomeEntryStatus.Recorded) => new()
        {
            OrganizationId = _orgId,
            Kind = IncomeEntryKind.SubscriptionCharge,
            SourceKind = IncomeSourceKind.SubscriptionBilling,
            SourceRef = sourceRef,
            ChargeBasis = IncomeChargeBasis.Derived,
            Status = status,
            Amount = amount,
            Reason = "A reason long enough to satisfy the rule.",
            OccurredAt = DateTime.UtcNow,
            RecordedByUserId = Guid.CreateVersion7(),
        };

    [Fact]
    public async Task TheMigrationAppliesAndTheTableExists()
    {
        await using var context = new AppDbContext(_options);

        var count = await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM \"IncomeLedgerEntries\"")
            .SingleAsync();

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TheAmountCheckConstraint_RejectsANonPositiveAmount(decimal amount)
    {
        await using var context = new AppDbContext(_options);
        context.IncomeLedgerEntries.Add(Entry(amount, $"constraint-{Guid.NewGuid():N}"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("CK_IncomeLedgerEntries_Amount", Describe(exception));
    }

    [Fact]
    public async Task TheDedupIndex_RejectsASecondLiveEntryForTheSameIdentity()
    {
        var sourceRef = $"period-{Guid.NewGuid():N}";
        await using (var first = new AppDbContext(_options))
        {
            first.IncomeLedgerEntries.Add(Entry(1000m, sourceRef));
            await first.SaveChangesAsync();
        }

        await using var second = new AppDbContext(_options);
        second.IncomeLedgerEntries.Add(Entry(2000m, sourceRef));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Contains("duplicate", Describe(exception), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The index is partial on a non-null <c>SourceRef</c>, because a row without one has no
    /// identity to dedupe on. Without the filter, two such rows would collide.
    /// </summary>
    [Fact]
    public async Task TheDedupIndex_IsPartialOnANonNullSourceRef()
    {
        await using var context = new AppDbContext(_options);
        var first = Entry(1000m, "ignored");
        first.SourceRef = null;
        var second = Entry(1000m, "ignored");
        second.SourceRef = null;

        context.IncomeLedgerEntries.AddRange(first, second);

        await context.SaveChangesAsync();
        Assert.Equal(2, await context.IncomeLedgerEntries.CountAsync());
    }

    /// <summary>
    /// A nulled entry frees its identity, which is what makes the derived/verified supersede rule
    /// work: the verified receipt takes over the reference its derived counterpart held.
    /// </summary>
    /// <remarks>
    /// This drives the **real service**, because two things it must do are only observable against
    /// a real database and neither is the ordering EF would choose:
    ///
    /// 1. EF sorts modification commands by entity type then by state, putting the INSERT ahead of
    ///    the UPDATE, so the void must be flushed first inside a transaction.
    /// 2. The filtered unique index does not look at `Status`, so a voided row whose `SourceRef` is
    ///    still populated **keeps occupying the key** and the takeover trips `23505`. The void must
    ///    therefore clear the reference as well.
    ///
    /// Both were found by this test failing; a hand-rolled two-save fixture would have masked
    /// exactly the bugs it exists to catch.
    /// </remarks>
    [Fact]
    public async Task TheService_SupersedesADerivedEntryWithAVerifiedReceipt()
    {
        var sourceRef = $"period-{Guid.NewGuid():N}";
        var actor = Guid.CreateVersion7();
        Guid derivedId;

        await using (var context = new AppDbContext(_options))
        {
            var service = new IncomeLedgerService(context, NullLogger<IncomeLedgerService>.Instance);
            var derived = await service.RecordAsync(Charge(
                sourceRef, IncomeChargeBasis.Derived, actor));
            derivedId = derived.Id;

            var verified = await service.RecordAsync(Charge(
                sourceRef, IncomeChargeBasis.Verified, actor, supersedes: derivedId));

            Assert.Equal(derivedId, verified.SupersedesEntryId);
        }

        await using var verify = new AppDbContext(_options);
        var derivedRow = await verify.IncomeLedgerEntries.SingleAsync(e => e.Id == derivedId);

        // Append-only: both rows survive, and the derived one is nulled rather than deleted. The
        // reference is released so the identity is free, and the link survives on the new row.
        Assert.Equal(2, await verify.IncomeLedgerEntries.CountAsync(e => e.SupersedesEntryId == derivedId || e.Id == derivedId));
        Assert.Equal(IncomeEntryStatus.Voided, derivedRow.Status);
        Assert.Null(derivedRow.SourceRef);

        var verifiedRow = await verify.IncomeLedgerEntries
            .SingleAsync(e => e.SupersedesEntryId == derivedId);
        Assert.Equal(IncomeEntryStatus.Recorded, verifiedRow.Status);
        Assert.Equal(sourceRef, verifiedRow.SourceRef);
        Assert.Equal(IncomeChargeBasis.Verified, verifiedRow.ChargeBasis);
    }

    [Fact]
    public async Task TheService_RejectsALiveDuplicate_OnARealDatabase()
    {
        var sourceRef = $"dup-{Guid.NewGuid():N}";
        var actor = Guid.CreateVersion7();

        await using var context = new AppDbContext(_options);
        var service = new IncomeLedgerService(context, NullLogger<IncomeLedgerService>.Instance);
        await service.RecordAsync(Charge(sourceRef, IncomeChargeBasis.Derived, actor));

        await Assert.ThrowsAsync<DuplicateRevenueEntryException>(
            () => service.RecordAsync(Charge(sourceRef, IncomeChargeBasis.Derived, actor)));
    }

    private RecordIncomeCommand Charge(
        string sourceRef,
        IncomeChargeBasis basis,
        Guid actor,
        Guid? supersedes = null) => new(
            OrganizationId: _orgId,
            Amount: 5000m,
            Reason: "Monthly plan charge for the period.",
            Kind: IncomeEntryKind.SubscriptionCharge,
            ChargeBasis: basis,
            SourceKind: IncomeSourceKind.SubscriptionBilling,
            SourceRef: sourceRef,
            PeriodStart: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            OccurredAt: DateTime.UtcNow,
            RecordedByUserId: actor,
            SupersedesEntryId: supersedes);

    [Fact]
    public async Task Amount_KeepsTwoDecimalPlacesAcrossARoundTrip()
    {
        await using var context = new AppDbContext(_options);
        var entry = Entry(1234.56m, $"roundtrip-{Guid.NewGuid():N}");
        context.IncomeLedgerEntries.Add(entry);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloaded = await context.IncomeLedgerEntries.SingleAsync(e => e.Id == entry.Id);

        Assert.Equal(1234.56m, reloaded.Amount);
    }

    /// <summary>
    /// A third decimal place is not rejected, it is rounded by PostgreSQL to the column's scale —
    /// which is exactly why `decimal(18,2)` rather than the Blossom ledger's `(18,4)` is asserted
    /// in the model test: a half-cent cannot survive a round trip here.
    /// </summary>
    [Fact]
    public async Task AThirdDecimalPlace_IsRoundedToTwoPlaces()
    {
        await using var context = new AppDbContext(_options);
        var entry = Entry(10.005m, $"scale-{Guid.NewGuid():N}");
        context.IncomeLedgerEntries.Add(entry);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloaded = await context.IncomeLedgerEntries.SingleAsync(e => e.Id == entry.Id);

        Assert.Equal(10.01m, reloaded.Amount);
    }

    private static string Describe(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }
        return string.Join(" | ", messages);
    }
}
