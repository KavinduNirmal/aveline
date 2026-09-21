using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T4 — the **reduced takings read** (E-13) and the revenue series (E-2).
///
/// E-13 is the answer to "staff see total earnings, reduced". Its whole design is that the reduction
/// is **enforced by the shape of the response, not by a UI convention**: the DTO carries exactly
/// `collected` and `billedUnconfirmed` and nothing else, so a later addition that leaked margin or a
/// per-client split would fail the field-enumeration test below rather than quietly shipping.
/// </summary>
public class TenantTakingsAndSeriesTests
{
    private readonly AppDbContext _context;
    private readonly TenantDashboardService _service;
    private readonly BoutiqueSaleLedgerService _ledger;
    private readonly Guid _orgId;

    public TenantTakingsAndSeriesTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Takings_{Guid.NewGuid()}")
            .Options);
        _service = new TenantDashboardService(_context, null, NullLogger<TenantDashboardService>.Instance);
        _ledger = new BoutiqueSaleLedgerService(_context, NullLogger<BoutiqueSaleLedgerService>.Instance);
        _orgId = SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<Guid> SeedAsync()
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"tk_{ownerId:N}", Email = "tk@aveline.lk",
            FirstName = "T", LastName = "K", Username = $"tk_{ownerId:N}",
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

    private static readonly Guid Actor = Guid.CreateVersion7();

    private Task<BoutiqueSaleEntry> RecordAsync(
        BoutiqueSaleEntryKind kind, BoutiqueSaleChargeBasis basis, decimal amount, DateTime? at = null)
        => _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            _orgId, amount, $"Entry recorded for {kind}.",
            kind, basis, BoutiqueSaleSourceKind.CounterWalkIn, $"ref:{Guid.NewGuid():N}",
            at ?? DateTime.UtcNow.AddDays(-1), Actor));

    // ── E-13: the reduced shape ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheReducedRead_CarriesExactlyTwoMoneyFigures()
    {
        await RecordAsync(BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified, 10000m);
        await RecordAsync(BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Derived, 30000m);

        var takings = await _service.GetTakingsAsync(_orgId, "30d", CancellationToken.None);

        Assert.Equal(10000m, takings.Collected);
        Assert.Equal(30000m, takings.BilledUnconfirmed);
    }

    /// <summary>
    /// The leak guard. If a future change adds margin, top items, a per-client split or a series to
    /// the reduced read, this fails — the reduction is a contract, not a rendering choice.
    /// </summary>
    [Fact]
    public async Task TheReducedRead_SerialisesOnlyTheFieldsItIsAllowedTo()
    {
        await RecordAsync(BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified, 10000m);

        var takings = await _service.GetTakingsAsync(_orgId, "30d", CancellationToken.None);
        var json = JsonSerializer.Serialize(takings);
        using var document = JsonDocument.Parse(json);
        // Record `Name` is PascalCase while the API serialises camelCase, so the comparison is
        // case-insensitive: the contract is the field set, not its casing.
        var fields = document.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "window", "windowFrom", "windowTo", "generatedAt", "currency",
            "collected", "billedUnconfirmed", "paymentRowsPresent", "ledgerBackfilled",
            "dataQuality", "invalidReason",
        };

        Assert.True(
            fields.IsSubsetOf(allowed),
            $"The reduced read leaked: {string.Join(", ", fields.Except(allowed, StringComparer.OrdinalIgnoreCase))}");

        // The fields that would be a leak are named explicitly, so the intent is legible. The check
        // is against the **field names**, not the whole payload: the quality notes legitimately use
        // the word "sales" in a sentence, and a substring scan over prose would fire on it.
        foreach (var forbidden in new[] { "margin", "marginPercent", "topItems", "sales", "points", "series", "customerId", "orderId" })
        {
            Assert.DoesNotContain(forbidden, fields, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TheReducedRead_SubtractsRefundsFromCollected()
    {
        await RecordAsync(BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified, 10000m);
        await RecordAsync(BoutiqueSaleEntryKind.Refund, BoutiqueSaleChargeBasis.Verified, 2500m);

        var takings = await _service.GetTakingsAsync(_orgId, "30d", CancellationToken.None);

        Assert.Equal(7500m, takings.Collected);
    }

    [Fact]
    public async Task TheReducedRead_OnAnEmptyBoutique_IsZeroWithAStatedReason()
    {
        var takings = await _service.GetTakingsAsync(_orgId, "30d", CancellationToken.None);

        // No ledger entries is genuinely zero money, and the quality block says why it looks empty.
        Assert.Equal(0m, takings.Collected);
        Assert.Equal(0m, takings.BilledUnconfirmed);
        Assert.False(takings.PaymentRowsPresent);
        Assert.NotEmpty(takings.DataQuality.Notes);
    }

    [Fact]
    public async Task TheReducedRead_RejectsAnUnknownWindow()
    {
        var takings = await _service.GetTakingsAsync(_orgId, "forever", CancellationToken.None);

        Assert.NotNull(takings.InvalidReason);
    }

    [Fact]
    public async Task TheReducedRead_IsScopedToTheOrganization()
    {
        var otherOwner = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = otherOwner, ClerkId = $"tk2_{otherOwner:N}", Email = "tk2@aveline.lk",
            FirstName = "T2", LastName = "K2", Username = $"tk2_{otherOwner:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var otherOrg = new Organization
        {
            Name = "Other", Slug = $"tk2-{otherOwner:N}", OwnerUserId = otherOwner,
        };
        _context.Organizations.Add(otherOrg);
        await _context.SaveChangesAsync();
        await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            otherOrg.Id, 999999m, "Another boutique's takings.",
            BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified,
            BoutiqueSaleSourceKind.CounterWalkIn, "other-ref", DateTime.UtcNow, Actor));

        var takings = await _service.GetTakingsAsync(_orgId, "30d", CancellationToken.None);

        Assert.Equal(0m, takings.Collected);
    }

    // ── E-2: the revenue series ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSeries_IsDenseAndAGapIsNullRatherThanZero()
    {
        var to = DateTime.UtcNow.Date.AddDays(1);
        var from = to.AddDays(-5);
        // One order on day 2; no orders on the other days.
        _context.Orders.Add(new Order
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = _orgId,
            CustomerId = Guid.CreateVersion7(),
            CustomerName = "Series Client",
            Status = "completed",
            Subtotal = 5000m,
            Total = 5000m,
            TotalCost = 3000m,
            CreatedAt = from.AddDays(2).AddHours(12),
        });
        await _context.SaveChangesAsync();

        var series = await _service.GetRevenueSeriesAsync(_orgId, from, to, "day", CancellationToken.None);

        Assert.Equal(5, series.Points.Count);
        Assert.Single(series.Points, point => point.GrossOrderValue == 5000m);
        // A bucket with no orders is `null`, so the chart renders a gap instead of a line drawn
        // through a measurement the server never produced.
        Assert.Equal(4, series.Points.Count(point => point.GrossOrderValue is null));
    }

    [Fact]
    public async Task TheSeries_ReportsACappedWindowRatherThanClampingSilently()
    {
        var to = DateTime.UtcNow;
        var series = await _service.GetRevenueSeriesAsync(
            _orgId, to.AddDays(-400), to, "month", CancellationToken.None);

        Assert.True(series.WindowCapped);
        Assert.Contains(
            series.Points,
            _ => true);
    }

    [Fact]
    public async Task TheSeries_RejectsAnUnknownBucket()
    {
        var series = await _service.GetRevenueSeriesAsync(
            _orgId, null, null, "fortnight", CancellationToken.None);

        Assert.NotNull(series.InvalidReason);
    }

    [Fact]
    public async Task TheSeries_ExcludesTheTerminalNegativeStatuses()
    {
        var to = DateTime.UtcNow.Date.AddDays(1);
        var from = to.AddDays(-3);
        _context.Orders.AddRange(
            new Order
            {
                Id = Guid.CreateVersion7(), OrganizationId = _orgId,
                CustomerId = Guid.CreateVersion7(), CustomerName = "C",
                Status = "completed", Subtotal = 1000m, Total = 1000m, TotalCost = 600m,
                CreatedAt = from.AddHours(1),
            },
            new Order
            {
                Id = Guid.CreateVersion7(), OrganizationId = _orgId,
                CustomerId = Guid.CreateVersion7(), CustomerName = "C",
                Status = "cancelled", Subtotal = 9999m, Total = 9999m, TotalCost = 0m,
                CreatedAt = from.AddHours(2),
            });
        await _context.SaveChangesAsync();

        var series = await _service.GetRevenueSeriesAsync(_orgId, from, to, "day", CancellationToken.None);

        Assert.Equal(1000m, series.Points.Sum(point => point.GrossOrderValue ?? 0m));
    }

    // ── E-3: top items ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TopItems_GroupsOnTheDenormalisedItemName()
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = _orgId,
            CustomerId = Guid.CreateVersion7(),
            CustomerName = "Top Client",
            Status = "completed",
            Subtotal = 9000m,
            Total = 9000m,
            TotalCost = 5000m,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
        };
        _context.Orders.Add(order);
        _context.OrderItems.AddRange(
            new OrderItem
            {
                Id = Guid.CreateVersion7(), OrganizationId = _orgId, OrderId = order.Id,
                ItemId = Guid.CreateVersion7(), ItemName = "Silk Saree", Quantity = 2, UnitPrice = 3000m,
                WholesaleCost = 1500m, TotalPrice = 6000m,
            },
            new OrderItem
            {
                Id = Guid.CreateVersion7(), OrganizationId = _orgId, OrderId = order.Id,
                ItemId = Guid.CreateVersion7(), ItemName = "Bridal Blouse", Quantity = 1, UnitPrice = 3000m,
                WholesaleCost = 1500m, TotalPrice = 3000m,
            });
        await _context.SaveChangesAsync();

        var top = await _service.GetTopItemsAsync(_orgId, "30d", 5, CancellationToken.None);

        Assert.Equal(2, top.Items.Count);
        Assert.Equal("Silk Saree", top.Items[0].ItemName);
        Assert.Equal(2, top.Items[0].Quantity);
        Assert.Equal(6000m, top.Items[0].Revenue);
    }

    [Fact]
    public async Task TopItems_ClampsTheLimit()
    {
        var tooBig = await _service.GetTopItemsAsync(_orgId, "30d", 500, CancellationToken.None);
        var tooSmall = await _service.GetTopItemsAsync(_orgId, "30d", 0, CancellationToken.None);

        Assert.Equal(20, tooBig.Limit);
        Assert.Equal(5, tooSmall.Limit);
    }

    [Fact]
    public async Task TopItems_RejectsAnUnknownWindow()
    {
        var top = await _service.GetTopItemsAsync(_orgId, "eternity", 5, CancellationToken.None);

        Assert.NotNull(top.InvalidReason);
    }
}
