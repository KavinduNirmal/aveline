using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R1 (issue #342) — the income ledger's write rules and its append-only surface.
///
/// Every rule lives in <see cref="IncomeLedgerService"/> so no write site can re-implement one and
/// drift. The two that carry the most weight are the actor requirement (an unauditable money entry
/// is refused, mirroring the entitlement-override route) and the derived/verified supersede rule,
/// which is what stops the same charge being counted twice.
/// </summary>
public class IncomeLedgerServiceTests
{
    private readonly AppDbContext _context;
    private readonly IncomeLedgerService _service;

    public IncomeLedgerServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Income_{Guid.NewGuid()}")
            .Options);
        _service = new IncomeLedgerService(_context, NullLogger<IncomeLedgerService>.Instance);
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
            Name = "Revenue Boutique", Slug = $"rb-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private static RecordIncomeCommand Command(
        Guid organizationId,
        decimal amount = 5000m,
        string reason = "Monthly plan charge for the period.",
        IncomeEntryKind kind = IncomeEntryKind.SubscriptionCharge,
        IncomeChargeBasis basis = IncomeChargeBasis.Derived,
        IncomeSourceKind sourceKind = IncomeSourceKind.SubscriptionBilling,
        string? sourceRef = null,
        Guid? recordedBy = null,
        Guid? supersedes = null) => new(
            organizationId,
            amount,
            reason,
            kind,
            basis,
            sourceKind,
            // A `SourceRef` is mandatory, so the helper supplies a unique one unless a test is
            // deliberately exercising the dedup identity.
            sourceRef ?? $"ref-{Guid.NewGuid():N}",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTime.UtcNow,
            recordedBy ?? Guid.CreateVersion7(),
            supersedes);

    // ── Validation ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5000)]
    public async Task Amount_MustBePositive(decimal amount)
    {
        var orgId = await SeedOrganizationAsync();

        var exception = await Assert.ThrowsAsync<RevenueValidationException>(
            () => _service.RecordAsync(Command(orgId, amount: amount)));

        Assert.Contains("positive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reason_ShorterThanTenCharacters_IsRejected()
    {
        var orgId = await SeedOrganizationAsync();

        await Assert.ThrowsAsync<RevenueValidationException>(
            () => _service.RecordAsync(Command(orgId, reason: "too short")));
    }

    [Fact]
    public async Task Reason_OfExactlyTenCharacters_IsAccepted()
    {
        var orgId = await SeedOrganizationAsync();

        // The rule is 10..500 inclusive, matching the Blossom ledger's `length(reason) >= 10`
        // check constraint. An off-by-one here would reject a legitimate short note.
        var entry = await _service.RecordAsync(Command(orgId, reason: "0123456789"));

        Assert.Equal("0123456789", entry.Reason);
    }

    [Fact]
    public async Task Reason_LongerThanFiveHundredCharacters_IsRejected()
    {
        var orgId = await SeedOrganizationAsync();

        await Assert.ThrowsAsync<RevenueValidationException>(
            () => _service.RecordAsync(Command(orgId, reason: new string('x', 501))));
    }

    [Fact]
    public async Task UnknownOrganization_IsRejected()
    {
        var missing = Guid.CreateVersion7();

        await Assert.ThrowsAsync<RevenueOrganizationNotFoundException>(
            () => _service.RecordAsync(Command(missing)));
    }

    /// <summary>
    /// An entry with no actor cannot be attributed to anyone, which is the whole reason the ledger
    /// exists in an audited console. `AdminOrganizationEndpoints` already refuses an unauditable
    /// entitlement change for the same reason.
    /// </summary>
    [Fact]
    public async Task Entry_WithNoRecordedActor_IsRejected()
    {
        var orgId = await SeedOrganizationAsync();

        var exception = await Assert.ThrowsAsync<RevenueValidationException>(
            () => _service.RecordAsync(Command(orgId, recordedBy: Guid.Empty)));

        Assert.Contains("actor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Entry_WithNoSourceRef_IsRejected()
    {
        var orgId = await SeedOrganizationAsync();

        // `SourceRef` is the dedup identity, so an entry without one can never be reconciled
        // against the thing that produced it.
        await Assert.ThrowsAsync<RevenueValidationException>(
            () => _service.RecordAsync(Command(orgId, sourceRef: "   ")));
    }

    // ── The happy path and its stored shape ──────────────────────────────────────────────────

    [Fact]
    public async Task Record_PersistsAPositiveAmount_AndDerivesTheSignFromKind()
    {
        var orgId = await SeedOrganizationAsync();

        var entry = await _service.RecordAsync(Command(orgId, amount: 5000m));

        Assert.True(entry.Amount > 0);
        Assert.Equal(5000m, entry.Amount);
        Assert.Equal(IncomeEntryStatus.Recorded, entry.Status);
        Assert.Equal("LKR", entry.Currency);
        Assert.Equal(IncomeEntryKind.SubscriptionCharge, entry.Kind);
        Assert.Equal(IncomeChargeBasis.Derived, entry.ChargeBasis);
        Assert.Equal(1, await _context.IncomeLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Record_KeepsThePeriodForACharge()
    {
        var orgId = await SeedOrganizationAsync();

        var entry = await _service.RecordAsync(Command(orgId));

        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), entry.PeriodStart);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), entry.PeriodEnd);
    }

    // ── The supersede rule ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The rule that stops the two entry classes being double-counted. A verified receipt for the
    /// same `(SourceKind, SourceRef)` nulls its derived counterpart rather than sitting beside it,
    /// because the derived row was an *expectation* of the very charge that has now been collected.
    /// </summary>
    /// <remarks>
    /// The derived row is voided **and its `SourceRef` is cleared**. Both are required: the filtered
    /// unique index does not look at `Status`, so a voided row that still holds the reference keeps
    /// occupying the key. The historical link survives as `SupersedesEntryId` on the new row.
    /// </remarks>
    [Fact]
    public async Task VerifiedEntry_NullsItsDerivedCounterpart()
    {
        var orgId = await SeedOrganizationAsync();
        var sourceRef = "period-2026-09";
        var derived = await _service.RecordAsync(
            Command(orgId, sourceRef: sourceRef, basis: IncomeChargeBasis.Derived));

        var verified = await _service.RecordAsync(Command(
            orgId,
            sourceRef: sourceRef,
            basis: IncomeChargeBasis.Verified,
            supersedes: derived.Id));

        var reloaded = await _context.IncomeLedgerEntries
            .SingleAsync(e => e.Id == derived.Id);

        Assert.Equal(IncomeEntryStatus.Voided, reloaded.Status);
        Assert.Null(reloaded.SourceRef);
        Assert.Equal(derived.Id, verified.SupersedesEntryId);
        Assert.Equal(sourceRef, verified.SourceRef);
        // Append-only: both rows survive. The derived row is nulled, never deleted.
        Assert.Equal(2, await _context.IncomeLedgerEntries.CountAsync());
    }

    /// <summary>
    /// A charge that was voided **without being taken over** frees its reference for re-billing.
    /// </summary>
    /// <remarks>
    /// The contrast with <see cref="VerifiedEntry_NullsItsDerivedCounterpart"/> is the point: when a
    /// verified receipt takes over the reference, that reference is *still claimed* — by the
    /// receipt — so re-deriving the same charge is correctly a duplicate. It is only a void with no
    /// successor that releases the identity for a genuine re-bill.
    /// </remarks>
    [Fact]
    public async Task AChargeVoidedWithoutATakeover_FreesItsReferenceForARebill()
    {
        var orgId = await SeedOrganizationAsync();
        var sourceRef = "period-2026-09";

        var first = await _service.RecordAsync(Command(orgId, sourceRef: sourceRef));
        await _service.RecordAsync(Command(
            orgId,
            sourceRef: "adjustment-1",
            kind: IncomeEntryKind.Adjustment,
            basis: IncomeChargeBasis.Verified,
            supersedes: first.Id));

        // Nothing live holds the reference now, so re-billing the period is legitimate.
        var rebill = await _service.RecordAsync(Command(orgId, sourceRef: sourceRef));

        Assert.Equal(IncomeEntryStatus.Recorded, rebill.Status);
        Assert.Equal(sourceRef, rebill.SourceRef);
        var voided = await _context.IncomeLedgerEntries.SingleAsync(e => e.Id == first.Id);
        Assert.Equal(IncomeEntryStatus.Voided, voided.Status);
        Assert.Null(voided.SourceRef);
    }

    [Fact]
    public async Task ALiveDuplicate_IsRejected()
    {
        var orgId = await SeedOrganizationAsync();
        await _service.RecordAsync(Command(orgId, sourceRef: "period-2026-09"));

        var exception = await Assert.ThrowsAsync<DuplicateRevenueEntryException>(
            () => _service.RecordAsync(Command(orgId, sourceRef: "period-2026-09")));

        Assert.Equal("duplicate-revenue-entry", exception.Code);
    }

    [Fact]
    public async Task TheSameSourceRefForADifferentSourceKind_IsNotADuplicate()
    {
        var orgId = await SeedOrganizationAsync();
        await _service.RecordAsync(Command(
            orgId,
            sourceRef: "ref-1",
            sourceKind: IncomeSourceKind.SubscriptionBilling));

        // A top-up reference and a billing-period reference are different namespaces even when
        // the strings collide, which is why the index is on the pair.
        var entry = await _service.RecordAsync(Command(
            orgId,
            sourceRef: "ref-1",
            sourceKind: IncomeSourceKind.BlossomTopUp,
            kind: IncomeEntryKind.TopUpPurchase));

        Assert.Equal(IncomeSourceKind.BlossomTopUp, entry.SourceKind);
    }

    [Fact]
    public async Task Supersede_WithAMissingTarget_IsRejected()
    {
        var orgId = await SeedOrganizationAsync();

        await Assert.ThrowsAsync<IncomeLedgerEntryNotFoundException>(() => _service.RecordAsync(
            Command(orgId, sourceRef: "period-2026-09", supersedes: Guid.CreateVersion7())));
    }

    [Fact]
    public async Task Supersede_WithATargetFromAnotherOrganization_IsRejected()
    {
        var first = await SeedOrganizationAsync();
        var second = await SeedOrganizationAsync();
        var derived = await _service.RecordAsync(Command(first, sourceRef: "period-2026-09"));

        // Cross-tenant supersede would let one tenant null another's revenue row.
        await Assert.ThrowsAsync<IncomeLedgerEntryNotFoundException>(
            () => _service.RecordAsync(Command(
                second, sourceRef: "period-2026-09", supersedes: derived.Id)));
    }

    [Fact]
    public async Task Supersede_WithAnAlreadyVoidedTarget_IsRejected()
    {
        var orgId = await SeedOrganizationAsync();
        var sourceRef = "period-2026-09";
        var derived = await _service.RecordAsync(Command(orgId, sourceRef: sourceRef));
        await _service.RecordAsync(Command(
            orgId, sourceRef: sourceRef, basis: IncomeChargeBasis.Verified, supersedes: derived.Id));

        var exception = await Assert.ThrowsAsync<IncomeEntryNotVoidableException>(
            () => _service.RecordAsync(Command(
                orgId,
                sourceRef: "period-2026-10",
                basis: IncomeChargeBasis.Verified,
                supersedes: derived.Id)));

        Assert.Equal("income-entry-not-voidable", exception.Code);
    }

    // ── Append-only, structurally ────────────────────────────────────────────────────────────

    /// <summary>
    /// The append-only rule is structural rather than a convention: the service exposes no way to
    /// mutate an existing row, and the only state transition is a nulling that leaves the original
    /// on disk. This asserts the complete public surface, so adding an update method is a failure
    /// here rather than a quiet erosion of the rule.
    /// </summary>
    [Fact]
    public void TheServiceSurface_HasNoMutationMethod()
    {
        var methods = typeof(IIncomeLedgerService).GetMethods().Select(m => m.Name).Order().ToArray();

        Assert.Equal(new[] { "RecordAsync" }, methods);
    }

    [Fact]
    public async Task RecordingTwiceWithDifferentReferences_LeavesBothRowsOnDisk()
    {
        var orgId = await SeedOrganizationAsync();

        await _service.RecordAsync(Command(orgId, sourceRef: "period-2026-09"));
        await _service.RecordAsync(Command(orgId, sourceRef: "period-2026-10"));

        Assert.Equal(2, await _context.IncomeLedgerEntries.CountAsync());
    }
}
