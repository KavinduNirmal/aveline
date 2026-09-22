using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 — the fourth writer: an order that reaches a paid terminal status posts a **`Derived`** entry.
///
/// The basis matters more than the amount. A `Derived` entry means "this is what the order says was
/// sold" — billed value with no evidence that money moved. It is deliberately **not** posted when
/// the order already has a confirmed payment, because that path has already written a `Verified`
/// entry for the same money, and posting both would double-count one sale.
/// </summary>
public class OrderLedgerBridgeTests
{
    private readonly AppDbContext _context;
    private readonly BoutiqueSaleLedgerService _ledger;
    private readonly OrderService _orders;

    public OrderLedgerBridgeTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrderBridge_{Guid.NewGuid()}")
            .Options);
        _ledger = new BoutiqueSaleLedgerService(_context, NullLogger<BoutiqueSaleLedgerService>.Instance);
        _orders = new OrderService(
            new OrderRepository(_context),
            NullLogger<OrderService>.Instance,
            businessRulesService: null,
            approvalRepository: null,
            ledger: _ledger,
            paymentRepository: new PaymentRepository(_context));
    }

    private async Task<(Guid OrgId, Guid OrderId)> SeedAsync(
        string status, decimal total = 32000m, bool withConfirmedPayment = false)
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"ob_{ownerId:N}", Email = "ob@aveline.lk",
            FirstName = "O", LastName = "B", Username = $"ob_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Order Boutique", Slug = $"ob-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = org.Id,
            CustomerId = Guid.CreateVersion7(),
            CustomerName = "Order Client",
            Status = status,
            Subtotal = total,
            Total = total,
            TotalCost = total * 0.6m,
            CreatedAt = DateTime.UtcNow,
        };
        _context.Orders.Add(order);

        if (withConfirmedPayment)
        {
            _context.Payments.Add(new Payment
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = org.Id,
                OrderId = order.Id,
                Amount = total,
                PaymentType = "full",
                PaymentMethod = "card",
                Status = "confirmed",
                CreatedAt = DateTime.UtcNow,
                ConfirmedAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync();
        return (org.Id, order.Id);
    }

    [Fact]
    public async Task AnOrderReachingCompleted_WritesOneDerivedSaleEntry()
    {
        var (orgId, orderId) = await SeedAsync("delivered");

        await _orders.TransitionStatusAsync(
            orderId, orgId, new UpdateOrderStatusDto { Status = "completed" });

        var entry = Assert.Single(await _context.BoutiqueSaleEntries.AsNoTracking().ToListAsync());
        Assert.Equal(BoutiqueSaleEntryKind.Sale, entry.Kind);
        // Billed value, not money taken: nobody has confirmed that this was collected.
        Assert.Equal(BoutiqueSaleChargeBasis.Derived, entry.ChargeBasis);
        Assert.Equal(BoutiqueSaleSourceKind.OrderSettlement, entry.SourceKind);
        Assert.Equal(32000m, entry.Amount);
        Assert.Equal(orderId, entry.OrderId);
        Assert.Equal($"order:{orderId}", entry.SourceRef);
        // The order flow resolves no actor, so the row is unattributed rather than fabricated.
        Assert.Null(entry.RecordedByUserId);
    }

    [Fact]
    public async Task AnOrderReachingDelivered_AlsoPostsAnEntry()
    {
        var (orgId, orderId) = await SeedAsync("delivery_scheduled");

        await _orders.TransitionStatusAsync(
            orderId, orgId, new UpdateOrderStatusDto { Status = "delivered" });

        Assert.Equal(1, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task AnOrderWithAConfirmedPayment_PostsNoDerivedEntry()
    {
        // The payment confirmation already wrote a `Verified` row for this money. A derived row too
        // would count one sale twice, which is exactly the mistake the two-basis design prevents.
        var (orgId, orderId) = await SeedAsync("delivered", withConfirmedPayment: true);

        await _orders.TransitionStatusAsync(
            orderId, orgId, new UpdateOrderStatusDto { Status = "completed" });

        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task AnIntermediateTransition_PostsNothing()
    {
        var (orgId, orderId) = await SeedAsync("pending_hold");

        await _orders.TransitionStatusAsync(
            orderId, orgId, new UpdateOrderStatusDto { Status = "payment_requested" });

        // A status change that is not a settled sale is not a money event.
        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task CancellingAnOrder_PostsNothing()
    {
        var (orgId, orderId) = await SeedAsync("pending_hold");

        await _orders.CancelOrderAsync(orderId, orgId, "Client changed their mind.");

        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task RepeatingTheSameTransition_WritesNoSecondEntry()
    {
        var (orgId, orderId) = await SeedAsync("delivered");

        await _orders.TransitionStatusAsync(
            orderId, orgId, new UpdateOrderStatusDto { Status = "completed" });
        // A no-op transition returns early; the ledger must not gain a row for it.
        await _orders.TransitionStatusAsync(
            orderId, orgId, new UpdateOrderStatusDto { Status = "completed" });

        Assert.Equal(1, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task TheDerivedEntryOccurredAtTheTransition()
    {
        var (orgId, orderId) = await SeedAsync("delivered");
        var before = DateTime.UtcNow.AddSeconds(-1);

        await _orders.TransitionStatusAsync(
            orderId, orgId, new UpdateOrderStatusDto { Status = "completed" });

        var entry = await _context.BoutiqueSaleEntries.AsNoTracking().SingleAsync();
        Assert.True(entry.OccurredAt >= before);
    }
}
