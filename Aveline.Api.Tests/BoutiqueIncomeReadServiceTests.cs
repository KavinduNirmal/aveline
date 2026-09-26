using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 read half — the boutique income register.
///
/// Two rules carry the weight, and both are honesty rules rather than plumbing:
///
/// 1. **`Derived` and `Verified` are reported separately and never summed into one unlabelled
///    figure.** A derived total is billed value with no evidence of collection; a verified total is
///    money taken. The gap between them (`UnverifiedGap`) is the most useful number on the surface,
///    and netting it away would hide it.
/// 2. **`null` is never rendered as `0`.** An absent measure is absent.
/// </summary>
public class BoutiqueIncomeReadServiceTests
{
    private readonly AppDbContext _context;
    private readonly BoutiqueSaleLedgerService _ledger;
    private readonly BoutiqueIncomeReadService _reads;

    public BoutiqueIncomeReadServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"IncomeRead_{Guid.NewGuid()}")
            .Options);
        _ledger = new BoutiqueSaleLedgerService(_context, NullLogger<BoutiqueSaleLedgerService>.Instance);
        _reads = new BoutiqueIncomeReadService(_context);
    }

    private static readonly Guid Actor = Guid.CreateVersion7();

    private async Task<Guid> SeedOrganizationAsync(string suffix = "a")
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"ir_{suffix}_{ownerId:N}", Email = "ir@aveline.lk",
            FirstName = "I", LastName = "R", Username = $"ir_{suffix}_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = $"Income Read {suffix}", Slug = $"ir-{suffix}-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private Task<BoutiqueSaleEntry> RecordAsync(
        Guid orgId,
        decimal amount,
        BoutiqueSaleEntryKind kind,
        BoutiqueSaleChargeBasis basis,
        BoutiqueSaleSourceKind sourceKind,
        string sourceRef,
        DateTime? occurredAt = null) =>
        _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            orgId, amount, $"Ledger entry for {sourceRef}.",
            kind, basis, sourceKind, sourceRef,
            occurredAt ?? DateTime.UtcNow, Actor));

    // ── the register ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLedger_ReturnsTheWindowNewestFirst()
    {
        var orgId = await SeedOrganizationAsync();
        var now = DateTime.UtcNow;
        await RecordAsync(orgId, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn,
            "oldest", now.AddDays(-3));
        await RecordAsync(orgId, 2000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn,
            "newest", now.AddDays(-1));

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(
            From: now.AddDays(-7), To: now.AddDays(1), Page: 1, PageSize: 50));

        Assert.Equal(2, page.Total);
        Assert.Equal("newest", page.Items[0].SourceRef);
        Assert.Equal("oldest", page.Items[1].SourceRef);
    }

    [Fact]
    public async Task GetLedger_ExcludesEntriesOutsideTheWindow()
    {
        var orgId = await SeedOrganizationAsync();
        var now = DateTime.UtcNow;
        await RecordAsync(orgId, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn,
            "inside", now.AddDays(-2));
        await RecordAsync(orgId, 2000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn,
            "outside", now.AddDays(-40));

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(
            From: now.AddDays(-7), To: now.AddDays(1), Page: 1, PageSize: 50));

        Assert.Equal(1, page.Total);
        Assert.Equal("inside", Assert.Single(page.Items).SourceRef);
    }

    [Fact]
    public async Task GetLedger_IsScopedToTheOrganization()
    {
        var mine = await SeedOrganizationAsync("mine");
        var theirs = await SeedOrganizationAsync("theirs");
        await RecordAsync(mine, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "mine-1");
        await RecordAsync(theirs, 9000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "theirs-1");

        var page = await _reads.GetLedgerAsync(mine, new BoutiqueIncomeLedgerQuery(
            Page: 1, PageSize: 50));

        Assert.Equal(1, page.Total);
        Assert.Equal(1000m, page.Totals.SaleTotal);
    }

    [Fact]
    public async Task GetLedger_ClampsThePageSize()
    {
        var orgId = await SeedOrganizationAsync();

        var tooBig = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 5000));
        var tooSmall = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 0, PageSize: 0));

        // An omitted or non-positive `pageSize` takes the default rather than becoming 1, matching
        // the sibling ledger reader; an explicit oversized value is clamped.
        Assert.Equal(200, tooBig.PageSize);
        Assert.Equal(50, tooSmall.PageSize);
        Assert.Equal(1, tooSmall.Page);
    }

    [Fact]
    public async Task GetLedger_FiltersByKindAndBasis()
    {
        var orgId = await SeedOrganizationAsync();
        await RecordAsync(orgId, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "v-sale");
        await RecordAsync(orgId, 2000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Derived, BoutiqueSaleSourceKind.OrderSettlement, "d-sale");
        await RecordAsync(orgId, 500m, BoutiqueSaleEntryKind.Refund,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.Refund, "refund", null);

        var verified = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(
            Basis: "Verified", Page: 1, PageSize: 50));
        var derived = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(
            Basis: "Derived", Page: 1, PageSize: 50));
        var refunds = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(
            Kind: "Refund", Page: 1, PageSize: 50));

        // A derived and a verified sale are 1,000 and 2,000; the refund is verified too, so the
        // verified filter legitimately returns two rows.
        Assert.Equal(2, verified.Total);
        Assert.Equal(1, derived.Total);
        Assert.Equal("refund", Assert.Single(refunds.Items).SourceRef);
    }

    [Fact]
    public async Task GetLedger_RejectsAnUnknownKindOrBasis()
    {
        var orgId = await SeedOrganizationAsync();

        var badKind = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Kind: "Nonsense"));
        var badBasis = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Basis: "Nonsense"));

        Assert.NotNull(badKind.InvalidReason);
        Assert.NotNull(badBasis.InvalidReason);
    }

    [Fact]
    public async Task GetLedger_SearchesTheReason()
    {
        var orgId = await SeedOrganizationAsync();
        await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            orgId, 1000m, "Silk saree sold at the counter today.",
            BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified,
            BoutiqueSaleSourceKind.CounterWalkIn, "saree", DateTime.UtcNow, Actor));
        await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            orgId, 2000m, "Bridal blouse alteration deposit taken.",
            BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified,
            BoutiqueSaleSourceKind.CounterWalkIn, "blouse", DateTime.UtcNow, Actor));

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(
            Query: "saree", Page: 1, PageSize: 50));

        Assert.Equal(1, page.Total);
        Assert.Contains("Silk saree", Assert.Single(page.Items).Reason);
    }

    // ── the two-basis rule, stated in the response ───────────────────────────────────────────────

    [Fact]
    public async Task GetLedger_ReportsTheTwoBasesSeparatelyAndNeverSummed()
    {
        var orgId = await SeedOrganizationAsync();
        await RecordAsync(orgId, 10000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "counter");
        await RecordAsync(orgId, 30000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Derived, BoutiqueSaleSourceKind.OrderSettlement, "order");

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 50));

        Assert.Equal(10000m, page.Reconciliation.VerifiedTotal);
        Assert.Equal(30000m, page.Reconciliation.DerivedTotal);
        // The gap is reported, not hidden: it is the difference between what the shop billed and
        // what it has evidence of collecting.
        Assert.Equal(30000m, page.Reconciliation.UnverifiedGap);
        Assert.False(page.Reconciliation.IsReconciled);
        // And the two bases are never presented as one figure.
        Assert.NotEqual(
            page.Reconciliation.VerifiedTotal + page.Reconciliation.DerivedTotal,
            page.Reconciliation.NetVerified);
    }

    [Fact]
    public async Task GetLedger_NetVerifiedSubtractsRefunds()
    {
        var orgId = await SeedOrganizationAsync();
        await RecordAsync(orgId, 10000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "sale");
        await RecordAsync(orgId, 2500m, BoutiqueSaleEntryKind.Refund,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.Refund, "refund");

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 50));

        Assert.Equal(2500m, page.Reconciliation.RefundTotal);
        Assert.Equal(7500m, page.Reconciliation.NetVerified);
    }

    [Fact]
    public async Task GetLedger_TotalsCoverTheWholeWindow_NotThePage()
    {
        var orgId = await SeedOrganizationAsync();
        for (var i = 0; i < 5; i++)
        {
            await RecordAsync(orgId, 1000m, BoutiqueSaleEntryKind.Sale,
                BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn,
                $"sale-{i}", DateTime.UtcNow.AddMinutes(-i));
        }

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 2));

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(5, page.Total);
        // A page total would have said 2,000 and misreported the week.
        Assert.Equal(5000m, page.Totals.SaleTotal);
    }

    [Fact]
    public async Task GetLedger_ReportsEachKindInItsOwnTotal()
    {
        var orgId = await SeedOrganizationAsync();
        await RecordAsync(orgId, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "s");
        await RecordAsync(orgId, 4000m, BoutiqueSaleEntryKind.PaymentReceived,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.OrderPayment, "p");
        await RecordAsync(orgId, 300m, BoutiqueSaleEntryKind.Refund,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.Refund, "r");
        await RecordAsync(orgId, 50m, BoutiqueSaleEntryKind.Adjustment,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.System, "a");

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 50));

        Assert.Equal(1000m, page.Totals.SaleTotal);
        Assert.Equal(4000m, page.Totals.PaymentTotal);
        Assert.Equal(300m, page.Totals.RefundTotal);
        // An adjustment is a real correction, reported in its own total rather than netted.
        Assert.Equal(50m, page.Totals.AdjustmentTotal);
    }

    /// <summary>
    /// The flag is computed, not hardcoded. A register that contains repaired rows is telling the
    /// reader where the ledger begins, which is the difference between "we took this much" and "we
    /// have a record of this much".
    /// </summary>
    [Fact]
    public async Task GetLedger_ReportsWhetherTheRegisterContainsRepairedRows()
    {
        var orgId = await SeedOrganizationAsync();
        await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            orgId, 1000m, "Reconciled: payment abc was confirmed with no ledger entry.",
            BoutiqueSaleEntryKind.PaymentReceived, BoutiqueSaleChargeBasis.Verified,
            BoutiqueSaleSourceKind.OrderPayment, "payment:abc", DateTime.UtcNow, null));

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 50));

        Assert.True(page.DataQuality.IncomeLedgerBackfilled);
        Assert.Contains(
            page.DataQuality.Notes,
            note => note.Contains("begin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetLedger_OnAGenuinelyWrittenLedger_IsNotBackfilled()
    {
        var orgId = await SeedOrganizationAsync();
        await RecordAsync(orgId, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "normal");

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 50));

        Assert.False(page.DataQuality.IncomeLedgerBackfilled);
    }

    [Fact]
    public async Task GetLedger_OnAnEmptyShop_IsAllZeroesAndSaysSo()
    {
        var orgId = await SeedOrganizationAsync();

        var page = await _reads.GetLedgerAsync(orgId, new BoutiqueIncomeLedgerQuery(Page: 1, PageSize: 50));

        // A window with no entries is genuinely zero money, not "unmeasured": the ledger's own
        // completeness is what the quality block reports.
        Assert.Equal(0, page.Total);
        Assert.Equal(0m, page.Totals.SaleTotal);
        Assert.False(page.DataQuality.PaymentRowsPresent);
        Assert.False(page.DataQuality.IncomeLedgerBackfilled);
    }

    // ── the window cap is echoed, never silent ───────────────────────────────────────────────────

    [Fact]
    public async Task GetLedger_ReportsACappedWindowRatherThanSilentlyClamping()
    {
        var orgId = await SeedOrganizationAsync();
        var from = DateTime.UtcNow.AddDays(-400);
        var to = DateTime.UtcNow;

        var page = await _reads.GetLedgerAsync(
            orgId, new BoutiqueIncomeLedgerQuery(From: from, To: to, Page: 1, PageSize: 50));

        Assert.True(page.WindowCapped);
        Assert.True(page.Window.From > from);
        Assert.True(page.Window.To <= to);
        Assert.Contains(
            page.DataQuality.Notes,
            note => note.Contains("window", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetLedger_ReportsAnUncappedWindowAsUncapped()
    {
        var orgId = await SeedOrganizationAsync();
        var to = DateTime.UtcNow;
        var from = to.AddDays(-7);

        var page = await _reads.GetLedgerAsync(
            orgId, new BoutiqueIncomeLedgerQuery(From: from, To: to, Page: 1, PageSize: 50));

        Assert.False(page.WindowCapped);
        Assert.Equal(from, page.Window.From);
    }

    [Fact]
    public async Task GetLedger_RefusesAnInvertedWindow()
    {
        var orgId = await SeedOrganizationAsync();
        var to = DateTime.UtcNow;

        var page = await _reads.GetLedgerAsync(
            orgId, new BoutiqueIncomeLedgerQuery(From: to, To: to.AddDays(-1), Page: 1, PageSize: 50));

        Assert.NotNull(page.InvalidReason);
    }

    // ── the per-kind accounts breakdown ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccounts_BreaksTheWindowDownByKind()
    {
        var orgId = await SeedOrganizationAsync();
        await RecordAsync(orgId, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "s1");
        await RecordAsync(orgId, 2000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "s2");
        await RecordAsync(orgId, 500m, BoutiqueSaleEntryKind.Refund,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.Refund, "r1");

        var accounts = await _reads.GetAccountsAsync(orgId, new BoutiqueIncomeAccountsQuery());

        var sale = accounts.Items.Single(item => item.Kind == BoutiqueSaleEntryKind.Sale.ToString());
        Assert.Equal(3000m, sale.Total);
        Assert.Equal(2, sale.Count);
        var refund = accounts.Items.Single(item => item.Kind == BoutiqueSaleEntryKind.Refund.ToString());
        Assert.Equal(500m, refund.Total);
        Assert.Equal(1, refund.Count);
    }

    [Fact]
    public async Task GetAccounts_SplitsCashEntriesByPaymentMethod()
    {
        var orgId = await SeedOrganizationAsync();
        var orderId = Guid.CreateVersion7();
        foreach (var (method, amount) in new[] { ("cash", 1000m), ("card", 2500m) })
        {
            var paymentId = Guid.CreateVersion7();
            _context.Payments.Add(new Payment
            {
                Id = paymentId,
                OrganizationId = orgId,
                OrderId = orderId,
                Amount = amount,
                PaymentType = "full",
                PaymentMethod = method,
                Status = "confirmed",
                CreatedAt = DateTime.UtcNow,
                ConfirmedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();
            await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
                orgId, amount, $"Payment received by {method} for order {orderId}.",
                BoutiqueSaleEntryKind.PaymentReceived, BoutiqueSaleChargeBasis.Verified,
                BoutiqueSaleSourceKind.OrderPayment, $"payment:{paymentId}", DateTime.UtcNow, null,
                OrderId: orderId, PaymentId: paymentId));
        }

        var accounts = await _reads.GetAccountsAsync(orgId, new BoutiqueIncomeAccountsQuery());

        // The breakdown comes from the payment rows, not from a guess: the ledger entry carries no
        // method of its own, and inventing one would be the fabrication the whole surface avoids.
        Assert.Equal(1000m, accounts.ByPaymentMethod.Single(m => m.PaymentMethod == "cash").Total);
        Assert.Equal(2500m, accounts.ByPaymentMethod.Single(m => m.PaymentMethod == "card").Total);
    }

    [Fact]
    public async Task GetAccounts_IsScopedToTheOrganization()
    {
        var mine = await SeedOrganizationAsync("acc-mine");
        var theirs = await SeedOrganizationAsync("acc-theirs");
        await RecordAsync(mine, 1000m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "m");
        await RecordAsync(theirs, 9999m, BoutiqueSaleEntryKind.Sale,
            BoutiqueSaleChargeBasis.Verified, BoutiqueSaleSourceKind.CounterWalkIn, "t");

        var accounts = await _reads.GetAccountsAsync(mine, new BoutiqueIncomeAccountsQuery());

        Assert.Equal(1000m, accounts.Items.Single(item => item.Kind == "Sale").Total);
    }

    [Fact]
    public async Task GetAccounts_ReportsTheQualityBlock()
    {
        var orgId = await SeedOrganizationAsync();

        var accounts = await _reads.GetAccountsAsync(orgId, new BoutiqueIncomeAccountsQuery());

        Assert.NotNull(accounts.DataQuality);
        Assert.Equal("LKR", accounts.Currency);
        Assert.Contains(
            accounts.DataQuality.Notes,
            note => note.Contains("provider", StringComparison.OrdinalIgnoreCase));
    }
}
