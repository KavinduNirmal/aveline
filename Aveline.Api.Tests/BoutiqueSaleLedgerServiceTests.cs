using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 — the boutique income ledger's write rules.
///
/// This is the ledger of **the shop's own takings**, and it is a different economy from
/// <see cref="Modules.Revenue.Models.IncomeLedgerEntry"/>, which records what Aveline billed the
/// shop. The two tables are never read by each other's surface; a test below asserts that this
/// service writes only its own table.
///
/// The rules that carry the most weight:
/// <list type="bullet">
///   <item><b>The actor rule, copied verbatim from the sibling</b>: <c>RecordedByUserId</c> may be
///   null if and only if <c>SourceKind == System</c>. A human-triggered money entry that names
///   nobody is refused, because the journal must be able to answer "who did this?".</item>
///   <item><b>The two-basis rule</b>: a <c>Verified</c> entry supersedes a <c>Derived</c> one that
///   holds the same source reference, so the same money is never counted twice. The superseded row
///   is <b>voided, never deleted</b>.</item>
///   <item><b>The sign is derived from <c>Kind</c></b>, so <c>Amount</c> is always positive and a
///   column sum cannot net a refund against a sale by accident.</item>
/// </list>
/// </summary>
public class BoutiqueSaleLedgerServiceTests
{
    private readonly AppDbContext _context;
    private readonly BoutiqueSaleLedgerService _service;

    public BoutiqueSaleLedgerServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"BoutiqueSale_{Guid.NewGuid()}")
            .Options);
        _service = new BoutiqueSaleLedgerService(_context, NullLogger<BoutiqueSaleLedgerService>.Instance);
    }

    private async Task<Guid> SeedOrganizationAsync()
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"o_{ownerId:N}", Email = "o@aveline.lk",
            FirstName = "O", LastName = "W", Username = $"o_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Takings Boutique", Slug = $"tk-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private static RecordBoutiqueSaleCommand Command(
        Guid organizationId,
        decimal amount = 12500m,
        string reason = "Counter sale at the fitting desk.",
        BoutiqueSaleEntryKind kind = BoutiqueSaleEntryKind.Sale,
        BoutiqueSaleChargeBasis basis = BoutiqueSaleChargeBasis.Verified,
        BoutiqueSaleSourceKind sourceKind = BoutiqueSaleSourceKind.CounterWalkIn,
        string? sourceRef = null,
        Guid? recordedBy = null,
        Guid? supersedes = null) => new(
            organizationId,
            amount,
            reason,
            kind,
            basis,
            sourceKind,
            sourceRef,
            OccurredAt: DateTime.UtcNow,
            RecordedByUserId: recordedBy,
            OrderId: null,
            CustomerId: null,
            PaymentId: null,
            SupersedesEntryId: supersedes);

    // ── amount ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Record_RefusesANonPositiveAmount(decimal amount)
    {
        var orgId = await SeedOrganizationAsync();

        await Assert.ThrowsAsync<BoutiqueSaleValidationException>(() =>
            _service.RecordAsync(Command(orgId, amount: amount, sourceRef: "interaction:1")));
    }

    // ── reason ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Record_RefusesAReasonShorterThanTheFloor()
    {
        var orgId = await SeedOrganizationAsync();

        await Assert.ThrowsAsync<BoutiqueSaleValidationException>(() =>
            _service.RecordAsync(Command(orgId, reason: "too short", sourceRef: "interaction:1")));
    }

    [Fact]
    public async Task Record_RefusesAReasonLongerThanTheCeiling()
    {
        var orgId = await SeedOrganizationAsync();
        var huge = new string('x', 501);

        await Assert.ThrowsAsync<BoutiqueSaleValidationException>(() =>
            _service.RecordAsync(Command(orgId, reason: huge, sourceRef: "interaction:1")));
    }

    [Fact]
    public async Task Record_AcceptsAReasonAtTheFloorAndTheCeiling()
    {
        var orgId = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();

        var floor = await _service.RecordAsync(Command(
            orgId, reason: new string('a', 10), sourceRef: "interaction:floor", recordedBy: actor));
        var ceiling = await _service.RecordAsync(Command(
            orgId, reason: new string('b', 500), sourceRef: "interaction:ceiling", recordedBy: actor));

        Assert.NotNull(floor);
        Assert.NotNull(ceiling);
    }

    // ── the actor rule (copied verbatim from the sibling) ────────────────────────────────────────

    [Fact]
    public async Task Record_RefusesANullActorOnAHumanSourceKind()
    {
        var orgId = await SeedOrganizationAsync();

        var error = await Assert.ThrowsAsync<BoutiqueSaleValidationException>(() =>
            _service.RecordAsync(Command(
                orgId,
                sourceKind: BoutiqueSaleSourceKind.CounterWalkIn,
                sourceRef: "interaction:1",
                recordedBy: null)));
        Assert.Contains("actor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The **derived order entry** is the one writer with no human to name: `OrdersController`
    /// passes `createdBy: null` on create and the payment flow resolves no actor, so a settlement
    /// row cannot honestly name anybody. The sibling's actor rule permits a null actor for a
    /// system-written row, and `OrderSettlement` is exactly that. The distinction is preserved by
    /// requiring an actor on the two **counter** sources, which always have a person behind them.
    /// </summary>
    [Fact]
    public async Task Record_AllowsANullActorOnTheDerivedOrderSettlement()
    {
        var orgId = await SeedOrganizationAsync();

        var entry = await _service.RecordAsync(Command(
            orgId,
            kind: BoutiqueSaleEntryKind.Sale,
            basis: BoutiqueSaleChargeBasis.Derived,
            sourceKind: BoutiqueSaleSourceKind.OrderSettlement,
            sourceRef: "order:no-actor",
            recordedBy: null));

        Assert.Null(entry.RecordedByUserId);
        Assert.Equal(BoutiqueSaleChargeBasis.Derived, entry.ChargeBasis);
    }

    [Fact]
    public async Task Record_RefusesAnEmptyActorGuidOnAHumanSourceKind()
    {
        var orgId = await SeedOrganizationAsync();

        await Assert.ThrowsAsync<BoutiqueSaleValidationException>(() =>
            _service.RecordAsync(Command(
                orgId, sourceRef: "interaction:1", recordedBy: Guid.Empty)));
    }

    /// <summary>
    /// A refund is triggered at the counter, so it must name somebody. A payment confirmation is
    /// not: the payment flow resolves no actor at all, which is why `OrderPayment` is exempt.
    /// </summary>
    [Fact]
    public async Task Record_RefusesANullActorOnARefund()
    {
        var orgId = await SeedOrganizationAsync();

        await Assert.ThrowsAsync<BoutiqueSaleValidationException>(() =>
            _service.RecordAsync(Command(
                orgId,
                kind: BoutiqueSaleEntryKind.Refund,
                sourceKind: BoutiqueSaleSourceKind.Refund,
                sourceRef: "refund:payment-1",
                recordedBy: null)));
    }

    [Fact]
    public async Task Record_AllowsANullActorOnASystemSourceKind()
    {
        var orgId = await SeedOrganizationAsync();

        var entry = await _service.RecordAsync(Command(
            orgId,
            kind: BoutiqueSaleEntryKind.Sale,
            basis: BoutiqueSaleChargeBasis.Derived,
            sourceKind: BoutiqueSaleSourceKind.System,
            sourceRef: "order:abc",
            recordedBy: null));

        Assert.Null(entry.RecordedByUserId);
        Assert.Equal(BoutiqueSaleSourceKind.System, entry.SourceKind);
    }

    // ── dedup: a retried write produces one row ──────────────────────────────────────────────────

    [Fact]
    public async Task Record_RefusesASecondLiveEntryForTheSameSourceReference()
    {
        var orgId = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();

        await _service.RecordAsync(Command(
            orgId, sourceRef: "payment:42", recordedBy: actor));

        // A retried confirmation must not double-book the same money.
        await Assert.ThrowsAsync<DuplicateBoutiqueSaleEntryException>(() =>
            _service.RecordAsync(Command(
                orgId, sourceRef: "payment:42", recordedBy: actor)));
    }

    [Fact]
    public async Task Record_AllowsTheSameSourceReferenceInADifferentOrganization()
    {
        // Tenant isolation: the dedup identity is per organization, so two shops may each hold the
        // same provider reference without colliding.
        var mine = await SeedOrganizationAsync();
        var theirs = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();

        await _service.RecordAsync(Command(mine, sourceRef: "payment:7", recordedBy: actor));
        var second = await _service.RecordAsync(Command(theirs, sourceRef: "payment:7", recordedBy: actor));

        Assert.Equal(theirs, second.OrganizationId);
    }

    // ── the two-basis supersede rule ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AVerifiedEntry_SupersedesItsDerivedCounterpart()
    {
        var orgId = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();
        const string sourceRef = "order:9f2a";

        var derived = await _service.RecordAsync(Command(
            orgId,
            amount: 20000m,
            kind: BoutiqueSaleEntryKind.Sale,
            basis: BoutiqueSaleChargeBasis.Derived,
            sourceKind: BoutiqueSaleSourceKind.OrderSettlement,
            sourceRef: sourceRef,
            recordedBy: actor));

        var verified = await _service.RecordAsync(Command(
            orgId,
            amount: 20000m,
            kind: BoutiqueSaleEntryKind.PaymentReceived,
            basis: BoutiqueSaleChargeBasis.Verified,
            sourceKind: BoutiqueSaleSourceKind.OrderPayment,
            sourceRef: sourceRef,
            recordedBy: actor,
            supersedes: derived.Id));

        Assert.Equal(derived.Id, verified.SupersedesEntryId);

        var reloaded = await _context.BoutiqueSaleEntries
            .AsNoTracking()
            .FirstAsync(entry => entry.Id == derived.Id);
        // Voided, never deleted: the append-only rule means the original stays on disk.
        Assert.Equal(BoutiqueSaleEntryStatus.Voided, reloaded.Status);
        // And the dedup identity is released, so the filtered unique index does not reject the
        // takeover insert. The historical link survives on `SupersedesEntryId`.
        Assert.Null(reloaded.SourceRef);
    }

    [Fact]
    public async Task SupersedingAnAlreadyVoidedEntry_IsRefused()
    {
        var orgId = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();
        const string sourceRef = "order:already-voided";

        var derived = await _service.RecordAsync(Command(
            orgId, sourceRef: sourceRef,
            kind: BoutiqueSaleEntryKind.Sale,
            basis: BoutiqueSaleChargeBasis.Derived,
            sourceKind: BoutiqueSaleSourceKind.OrderSettlement,
            recordedBy: actor));

        await _service.RecordAsync(Command(
            orgId, sourceRef: sourceRef,
            kind: BoutiqueSaleEntryKind.PaymentReceived,
            basis: BoutiqueSaleChargeBasis.Verified,
            sourceKind: BoutiqueSaleSourceKind.OrderPayment,
            recordedBy: actor,
            supersedes: derived.Id));

        await Assert.ThrowsAsync<BoutiqueSaleEntryNotVoidableException>(() =>
            _service.RecordAsync(Command(
                orgId, sourceRef: sourceRef,
                kind: BoutiqueSaleEntryKind.PaymentReceived,
                basis: BoutiqueSaleChargeBasis.Verified,
                sourceKind: BoutiqueSaleSourceKind.OrderPayment,
                recordedBy: actor,
                supersedes: derived.Id)));
    }

    [Fact]
    public async Task SupersedingAnEntryInAnotherOrganization_IsRefused()
    {
        var mine = await SeedOrganizationAsync();
        var theirs = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();

        var theirsEntry = await _service.RecordAsync(Command(
            theirs,
            kind: BoutiqueSaleEntryKind.Sale,
            basis: BoutiqueSaleChargeBasis.Derived,
            sourceKind: BoutiqueSaleSourceKind.OrderSettlement,
            sourceRef: "order:cross-tenant",
            recordedBy: actor));

        // A cross-tenant supersede must read as "not found", never as a success.
        await Assert.ThrowsAsync<BoutiqueSaleEntryNotFoundException>(() =>
            _service.RecordAsync(Command(
                mine,
                kind: BoutiqueSaleEntryKind.PaymentReceived,
                basis: BoutiqueSaleChargeBasis.Verified,
                sourceKind: BoutiqueSaleSourceKind.OrderPayment,
                sourceRef: "order:cross-tenant",
                recordedBy: actor,
                supersedes: theirsEntry.Id)));
    }

    // ── the sign convention ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BoutiqueSaleEntryKind.Sale, 1)]
    [InlineData(BoutiqueSaleEntryKind.PaymentReceived, 1)]
    [InlineData(BoutiqueSaleEntryKind.Refund, -1)]
    public void TheSignOfAnEntry_IsDerivedFromItsKind(BoutiqueSaleEntryKind kind, int expectedSign)
    {
        Assert.Equal(expectedSign, BoutiqueSaleEntry.SignOf(kind));
    }

    [Fact]
    public async Task EveryStoredAmount_IsPositiveEvenForARefund()
    {
        var orgId = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();

        var refund = await _service.RecordAsync(Command(
            orgId,
            amount: 5000m,
            kind: BoutiqueSaleEntryKind.Refund,
            basis: BoutiqueSaleChargeBasis.Verified,
            sourceKind: BoutiqueSaleSourceKind.Refund,
            sourceRef: "refund:payment-1:1",
            recordedBy: actor));

        // The stored value is positive and the reader signs it by kind, so a naive column sum can
        // never net the refund against a sale by accident.
        Assert.Equal(5000m, refund.Amount);
        Assert.True(refund.Amount > 0m);
        Assert.Equal(-1, BoutiqueSaleEntry.SignOf(refund.Kind));
    }

    // ── the currency ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Record_StampsTheOrganizationCurrencyWhenAvailable()
    {
        var orgId = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();

        var entry = await _service.RecordAsync(Command(orgId, sourceRef: "sale:1", recordedBy: actor));

        // `Organization.Currency` is a real column (default LKR); the entry copies it rather than
        // hardcoding a literal, so a future multi-currency shop does not have to migrate the ledger.
        Assert.Equal("LKR", entry.Currency);
    }

    // ── the two ledgers stay separate (R-12) ─────────────────────────────────────────────────────

    [Fact]
    public async Task TheBoutiqueLedger_WritesOnlyItsOwnTable()
    {
        var orgId = await SeedOrganizationAsync();
        var actor = Guid.CreateVersion7();

        await _service.RecordAsync(Command(orgId, sourceRef: "sale:separation", recordedBy: actor));

        Assert.Equal(1, await _context.BoutiqueSaleEntries.CountAsync());
        // The admin revenue journal records what Aveline billed the shop. This service must never
        // reach it: two isomorphic org-scoped money ledgers in one codebase is the R-12 hazard.
        Assert.Equal(0, await _context.IncomeLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Record_RefusesANonExistentOrganization()
    {
        var actor = Guid.CreateVersion7();

        await Assert.ThrowsAsync<BoutiqueSaleOrganizationNotFoundException>(() =>
            _service.RecordAsync(Command(Guid.CreateVersion7(), sourceRef: "sale:1", recordedBy: actor)));
    }
}
