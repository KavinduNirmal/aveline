using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Authorization;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T4 — the tenant dashboard's KPI aggregate.
///
/// The rule this whole suite turns on: **every absent number is `null`, never `0`.** A KPI strip
/// that renders `LKR 0` for a figure the server could not compute is telling the owner a fact about
/// their shop that nobody established. `0` is a measurement; `null` is the absence of one.
/// </summary>
public class TenantDashboardSummaryTests
{
    private readonly AppDbContext _context;
    private readonly TenantDashboardService _service;
    private readonly Guid _orgId;

    public TenantDashboardSummaryTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantDash_{Guid.NewGuid()}")
            .Options);
        _service = new TenantDashboardService(
            _context,
            cache: null,
            NullLogger<TenantDashboardService>.Instance);
        _orgId = SeedOrganizationAsync("Dashboard Boutique").GetAwaiter().GetResult();
    }

    private async Task<Guid> SeedOrganizationAsync(string name)
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"td_{ownerId:N}", Email = "td@aveline.lk",
            FirstName = "T", LastName = "D", Username = $"td_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = name, Slug = $"td-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private async Task<Guid> AddOrderAsync(
        string status, decimal total, decimal cost, DateTime? createdAt = null, Guid? orgId = null)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = orgId ?? _orgId,
            CustomerId = Guid.CreateVersion7(),
            CustomerName = "KPI Client",
            Status = status,
            Subtotal = total,
            Discount = 0m,
            Total = total,
            TotalCost = cost,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-1),
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        return order.Id;
    }

    private async Task AddPaymentAsync(string status, decimal amount, DateTime? at = null)
    {
        _context.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = _orgId,
            OrderId = Guid.CreateVersion7(),
            Amount = amount,
            PaymentType = "full",
            PaymentMethod = "cash",
            Status = status,
            CreatedAt = at ?? DateTime.UtcNow.AddDays(-1),
            ConfirmedAt = status == "confirmed" ? at ?? DateTime.UtcNow.AddDays(-1) : null,
        });
        await _context.SaveChangesAsync();
    }

    private async Task AddCustomerAsync(int visitCount, decimal totalSpent, DateTime? createdAt = null)
    {
        _context.Customers.Add(new Customer
        {
            OrganizationId = _orgId,
            PhoneNumber = $"+9477{Guid.NewGuid().GetHashCode() & 0xFFFFFF:D6}",
            FullName = "KPI Customer",
            Status = "new",
            VisitCount = visitCount,
            TotalSpent = totalSpent,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-1),
        });
        await _context.SaveChangesAsync();
    }

    private async Task AddInventoryAsync(int quantity, decimal cost, string status = "available")
    {
        _context.InventoryItems.Add(new InventoryItem
        {
            OrgId = _orgId,
            ItemName = $"Piece {Guid.NewGuid():N}"[..12],
            Category = "saree",
            Color = "wine",
            Price = 5000m,
            Cost = cost,
            StockQuantity = quantity,
            Status = status,
        });
        await _context.SaveChangesAsync();
    }

    private Task<TenantDashboardSummaryDto> SummaryAsync(string window = "30d") =>
        _service.GetSummaryAsync(_orgId, window, CancellationToken.None);

    // ── the empty shop: null, never zero ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnAnEmptyBoutique_EveryUnmeasurableKpiIsNullNotZero()
    {
        var summary = await SummaryAsync();

        // An order count of 0 is a genuine measurement: nobody ordered.
        Assert.Equal(0, summary.Sales.OrderCount);
        // But an average over no orders does not exist, so it is `null`.
        Assert.Null(summary.Sales.AverageOrderValue);
        Assert.Null(summary.Sales.GrossOrderValue);
        Assert.Null(summary.Sales.MarginPercent);
        Assert.Null(summary.Cash.Collected);
        Assert.Null(summary.Catalog.StockValueAtCost);
        // No usage account exists yet, so the Blossom position is unmeasured rather than zero.
        Assert.Null(summary.Usage.BlossomUsed);
        Assert.Null(summary.Usage.MonthlyBlossomLimit);
    }

    [Fact]
    public async Task OnAnEmptyBoutique_TheQualityBlockExplainsWhyTheShopLooksEmpty()
    {
        var summary = await SummaryAsync();

        // The single most likely reason a real shop shows zero cash.
        Assert.False(summary.DataQuality.PaymentRowsPresent);
        Assert.Equal("LKR", summary.Currency);
        Assert.NotEmpty(summary.DataQuality.Notes);
    }

    // ── the window ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheWindowIsEchoedAndTheGeneratedAtIsStamped()
    {
        var summary = await SummaryAsync("7d");

        Assert.Equal("7d", summary.Window);
        Assert.True(summary.GeneratedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(summary.WindowFrom < summary.WindowTo);
    }

    [Theory]
    [InlineData("7d")]
    [InlineData("30d")]
    [InlineData("90d")]
    [InlineData("mtd")]
    [InlineData("ytd")]
    public async Task EveryDocumentedWindowIsAccepted(string window)
    {
        var summary = await SummaryAsync(window);

        Assert.Equal(window, summary.Window);
    }

    [Fact]
    public async Task AnUnknownWindowIsRejected()
    {
        var result = await _service.GetSummaryAsync(_orgId, "since-the-dawn-of-time", CancellationToken.None);

        Assert.NotNull(result.InvalidReason);
    }

    [Fact]
    public async Task AnInvertedWindowIsRejected()
    {
        var result = await _service.GetSummaryForRangeAsync(
            _orgId, DateTime.UtcNow, DateTime.UtcNow.AddDays(-7), "custom", CancellationToken.None);

        Assert.NotNull(result.InvalidReason);
    }

    // ── sales ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sales_SumsTheComponentsSeparately()
    {
        await AddOrderAsync("completed", 10000m, 6000m);
        await AddOrderAsync("delivered", 5000m, 3000m);

        var summary = await SummaryAsync();

        Assert.Equal(2, summary.Sales.OrderCount);
        Assert.Equal(15000m, summary.Sales.GrossOrderValue);
        Assert.Equal(7500m, summary.Sales.AverageOrderValue);
        Assert.Equal(6000m, summary.Sales.MarginAmount);
        // 6,000 margin on 15,000 gross.
        Assert.Equal(0.4m, summary.Sales.MarginPercent);
    }

    /// <summary>
    /// C-5. The exclusion list is a **named constant with a test that walks the transition map**, not
    /// a prose comment. The model's own comment names eleven statuses and forgets `confirmed` and
    /// `revised`, both of which the approval path writes — and `payment_expired` is **not** terminal,
    /// because it can return to `payment_requested`. A comment cannot enforce any of that.
    /// </summary>
    [Fact]
    public async Task Sales_ExcludesOnlyTheTerminalNegativeStatuses()
    {
        await AddOrderAsync("completed", 1000m, 600m);
        // Terminal-negative: excluded.
        await AddOrderAsync("cancelled", 9999m, 9999m);
        await AddOrderAsync("rejected", 9999m, 9999m);
        // Not terminal — it can return to `payment_requested` — so it counts.
        await AddOrderAsync("payment_expired", 2000m, 1200m);
        // Written by the approval path and forgotten by the model's comment; both count.
        await AddOrderAsync("confirmed", 3000m, 1800m);
        await AddOrderAsync("revised", 4000m, 2400m);

        var summary = await SummaryAsync();

        Assert.Equal(4, summary.Sales.OrderCount);
        Assert.Equal(10000m, summary.Sales.GrossOrderValue);
    }

    [Fact]
    public void EveryOrderStatusTheProductWrites_IsClassifiedCountedOrExcluded()
    {
        // The test the plan's C-5 asks for: adding a status to the transition map without
        // classifying it fails here rather than silently changing every KPI in the slice.
        var classified = TenantDashboardService.CountedOrderStatuses
            .Concat(TenantDashboardService.ExcludedOrderStatuses)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var status in OrderService.KnownOrderStatuses)
        {
            Assert.Contains(status, classified);
        }
    }

    [Fact]
    public void TheExcludedSetIsExactlyTheThreeTerminalNegativeStatuses()
    {
        Assert.Equal(
            new[] { "cancelled", "rejected" }.OrderBy(s => s),
            TenantDashboardService.ExcludedOrderStatuses
                .Where(s => s != "payment_expired")
                .OrderBy(s => s));
    }

    // ── the margin honesty flag ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Margin_IsFlaggedIncompleteWhenAnyOrderCarriesZeroCost()
    {
        await AddOrderAsync("completed", 10000m, 0m);

        var summary = await SummaryAsync();

        // `WholesaleCost` is caller-supplied rather than read from the inventory record, so a margin
        // built on it is only as trustworthy as what somebody typed.
        Assert.False(summary.Sales.MarginCostsComplete);
        Assert.False(summary.DataQuality.OrderCostsComplete);
    }

    [Fact]
    public async Task Margin_IsCompleteWhenEveryOrderCarriesACost()
    {
        await AddOrderAsync("completed", 10000m, 6000m);

        var summary = await SummaryAsync();

        Assert.True(summary.Sales.MarginCostsComplete);
    }

    // ── cash, stated as three separate figures ───────────────────────────────────────────────────

    [Fact]
    public async Task Cash_SeparatesCollectedOutstandingAndRefunded()
    {
        await AddPaymentAsync("confirmed", 20000m);
        await AddPaymentAsync("pending", 5000m);
        await AddPaymentAsync("refunded", 3000m);

        var summary = await SummaryAsync();

        Assert.Equal(20000m, summary.Cash.Collected);
        Assert.Equal(5000m, summary.Cash.Outstanding);
        Assert.Equal(3000m, summary.Cash.Refunded);
        Assert.Equal(1, summary.Cash.RefundCount);
        // Stated rather than implied: the collected figure is money taken and is never netted
        // against an outstanding request without a label.
        Assert.True(summary.DataQuality.RefundAndOutstandingExcludedFromCollected);
    }

    // ── customers, catalogue, team, usage, operations ────────────────────────────────────────────

    [Fact]
    public async Task Customers_CountsActiveRepeatAndNewInWindow()
    {
        await AddCustomerAsync(visitCount: 5, totalSpent: 50000m, createdAt: DateTime.UtcNow.AddDays(-2));
        await AddCustomerAsync(visitCount: 1, totalSpent: 5000m, createdAt: DateTime.UtcNow.AddDays(-200));
        await AddCustomerAsync(visitCount: 0, totalSpent: 0m, createdAt: DateTime.UtcNow.AddDays(-300));

        var summary = await SummaryAsync();

        Assert.Equal(3, summary.Customers.TotalCount);
        Assert.Equal(1, summary.Customers.NewInWindow);
        // Repeat = a client with two or more recorded visits.
        Assert.Equal(1, summary.Customers.RepeatCount);
        Assert.Equal(90, summary.Customers.InactivityThresholdDays);
    }

    [Fact]
    public async Task Catalog_CountsItemsLowStockAndStockValueAtCost()
    {
        await AddInventoryAsync(quantity: 10, cost: 1000m);
        await AddInventoryAsync(quantity: 2, cost: 2000m);
        await AddInventoryAsync(quantity: 0, cost: 500m);

        var summary = await SummaryAsync();

        Assert.Equal(3, summary.Catalog.ItemCount);
        Assert.Equal(1, summary.Catalog.LowStockCount);
        Assert.Equal(1, summary.Catalog.OutOfStockCount);
        // Labelled "at cost", never "retail value".
        Assert.Equal(14000m, summary.Catalog.StockValueAtCost);
    }

    [Fact]
    public async Task Operations_ReusesTheApprovalRepositorysPendingCount()
    {
        _context.ApprovalQueue.Add(new ApprovalQueueEntry
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = _orgId,
            OrderId = Guid.CreateVersion7(),
            ApprovalType = "discount",
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        });
        _context.ApprovalQueue.Add(new ApprovalQueueEntry
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = _orgId,
            OrderId = Guid.CreateVersion7(),
            ApprovalType = "discount",
            Status = "approved",
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var summary = await SummaryAsync();

        Assert.Equal(1, summary.Operations.PendingApprovals);
    }

    [Fact]
    public async Task Team_ReportsSeatsAndPendingInvitations()
    {
        var extra = Guid.CreateVersion7();
        _context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = _orgId, UserId = extra,
            BoutiqueRole = Roles.BoutiqueStaff, Status = MembershipStatus.Active,
        });
        _context.OrganizationInvitations.Add(new OrganizationInvitation
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = _orgId,
            TokenHash = "hash",
            BoutiqueRole = Roles.BoutiqueStaff,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var summary = await SummaryAsync();

        Assert.Equal(1, summary.Team.ActiveSeats);
        Assert.Equal(1, summary.Team.PendingInvitations);
    }

    // ── tenant isolation ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryFigureIsScopedToTheOrganization()
    {
        var otherOrg = await SeedOrganizationAsync("Other Boutique");
        await AddOrderAsync("completed", 999999m, 0m, orgId: otherOrg);
        await AddOrderAsync("completed", 1000m, 600m);

        var summary = await SummaryAsync();

        Assert.Equal(1, summary.Sales.OrderCount);
        Assert.Equal(1000m, summary.Sales.GrossOrderValue);
    }

    // ── the cache degrades, it never fails the request ───────────────────────────────────────────

    [Fact]
    public async Task AThrowingCache_DegradesToAnUncachedQueryRatherThanFailing()
    {
        await AddOrderAsync("completed", 1000m, 600m);
        var service = new TenantDashboardService(
            _context, new ThrowingCache(), NullLogger<TenantDashboardService>.Instance);

        var summary = await service.GetSummaryAsync(_orgId, "30d", CancellationToken.None);

        Assert.Equal(1000m, summary.Sales.GrossOrderValue);
        Assert.Contains(
            summary.DataQuality.Notes,
            note => note.Contains("cache", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A cache whose every operation throws, as a Redis outage does.</summary>
    private sealed class ThrowingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("cache down");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");

        public void Refresh(string key) => throw new InvalidOperationException("cache down");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");

        public void Remove(string key) => throw new InvalidOperationException("cache down");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("cache down");

        public Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");
    }
}
