using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 — the payment writers onto the boutique income ledger.
///
/// `PaymentService.ConfirmPaymentAsync` and `RefundPaymentAsync` were the only payment writers, and
/// they wrote **nothing else**: no ledger row, and on the refund path no row, no amount and no
/// timestamp at all — the `reason` parameter was read and then never used. This is the writer that
/// makes "money taken" a fact with a row behind it rather than an inference from a status column.
/// </summary>
public class PaymentLedgerBridgeTests
{
    private readonly AppDbContext _context;
    private readonly BoutiqueSaleLedgerService _ledger;
    private readonly PaymentService _payments;

    public PaymentLedgerBridgeTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PaymentBridge_{Guid.NewGuid()}")
            .Options);
        _ledger = new BoutiqueSaleLedgerService(_context, NullLogger<BoutiqueSaleLedgerService>.Instance);
        // These cases cover the counter path: a payment with no provider intent is a confirmation
        // an operator recorded, which Phase 9 keeps (plan §9.7 leaves the manual provider as the
        // honest adapter). The provider-backed path, where the intent is the only settlement
        // authority, is asserted in CommercePaymentsTests.
        _payments = new PaymentService(
            new PaymentRepository(_context),
            new OrderRepository(_context),
            _ledger,
            new FakePaymentIntentService(),
            Options.Create(new PaymentsOptions
            {
                Commerce = new CommercePaymentOptions { UseProviderIntents = true },
            }),
            _context);
    }

    /// <summary>The person the refund route attributed the refund to.</summary>
    private static readonly Guid RefundActor = Guid.CreateVersion7();

    private async Task<(Guid OrgId, Guid OrderId, Guid PaymentId)> SeedAsync(
        decimal amount = 25000m, string status = "pending")
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"p_{ownerId:N}", Email = "p@aveline.lk",
            FirstName = "P", LastName = "O", Username = $"p_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Payment Boutique", Slug = $"pb-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = org.Id,
            CustomerId = Guid.CreateVersion7(),
            CustomerName = "Nisha Fernando",
            Status = "payment_requested",
            Subtotal = amount,
            Total = amount,
            CreatedAt = DateTime.UtcNow,
        };
        _context.Orders.Add(order);

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = org.Id,
            OrderId = order.Id,
            Amount = amount,
            PaymentType = "full",
            PaymentMethod = "cash",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = status == "confirmed" ? DateTime.UtcNow : null,
        };
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        return (org.Id, order.Id, payment.Id);
    }

    [Fact]
    public async Task ConfirmingAPayment_WritesOneVerifiedPaymentReceivedEntry()
    {
        var (orgId, orderId, paymentId) = await SeedAsync();

        await _payments.ConfirmPaymentAsync(
            orgId, paymentId, new ConfirmPaymentDto { GatewayTransactionId = "txn-1" });

        var entry = Assert.Single(await _context.BoutiqueSaleEntries.AsNoTracking().ToListAsync());
        Assert.Equal(BoutiqueSaleEntryKind.PaymentReceived, entry.Kind);
        // A confirmation is the only durable signal that cash arrived, so it is `Verified`.
        Assert.Equal(BoutiqueSaleChargeBasis.Verified, entry.ChargeBasis);
        Assert.Equal(BoutiqueSaleSourceKind.OrderPayment, entry.SourceKind);
        Assert.Equal(25000m, entry.Amount);
        Assert.Equal(paymentId, entry.PaymentId);
        Assert.Equal(orderId, entry.OrderId);
        Assert.Equal($"payment:{paymentId}", entry.SourceRef);
    }

    [Fact]
    public async Task ConfirmingAnAlreadyConfirmedPayment_WritesNoSecondEntry()
    {
        var (orgId, _, paymentId) = await SeedAsync(status: "confirmed");

        // The confirmation path is idempotent by design: an already-confirmed payment returns the
        // existing row rather than re-confirming. The ledger must not gain a second entry either.
        await _payments.ConfirmPaymentAsync(
            orgId, paymentId, new ConfirmPaymentDto { GatewayTransactionId = "txn-again" });

        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task ConfirmingTwiceThroughTheSameFlow_WritesExactlyOneEntry()
    {
        var (orgId, _, paymentId) = await SeedAsync();

        await _payments.ConfirmPaymentAsync(
            orgId, paymentId, new ConfirmPaymentDto { GatewayTransactionId = "txn-1" });
        await _payments.ConfirmPaymentAsync(
            orgId, paymentId, new ConfirmPaymentDto { GatewayTransactionId = "txn-2" });

        Assert.Equal(1, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task RefundingAPayment_WritesOneVerifiedRefundEntry()
    {
        var (orgId, _, paymentId) = await SeedAsync(status: "confirmed");

        await _payments.RefundPaymentAsync(orgId, paymentId, "Client returned the saree.", ct: default, refundedByUserId: RefundActor);

        var entry = Assert.Single(await _context.BoutiqueSaleEntries.AsNoTracking().ToListAsync());
        Assert.Equal(BoutiqueSaleEntryKind.Refund, entry.Kind);
        Assert.Equal(BoutiqueSaleChargeBasis.Verified, entry.ChargeBasis);
        Assert.Equal(BoutiqueSaleSourceKind.Refund, entry.SourceKind);
        // Stored positive; the sign is the reader's job, so a column sum cannot net it against a sale.
        Assert.Equal(25000m, entry.Amount);
        Assert.Equal(-1, BoutiqueSaleEntry.SignOf(entry.Kind));
    }

    [Fact]
    public async Task TheRefundReason_ReachesTheLedgerRow()
    {
        var (orgId, _, paymentId) = await SeedAsync(status: "confirmed");

        // The `reason` parameter was accepted and then discarded by the old refund path. It now has
        // a destination, which is the difference between a refund and an unexplained balance change.
        await _payments.RefundPaymentAsync(orgId, paymentId, "Client returned the saree after a week.", ct: default, refundedByUserId: RefundActor);

        var entry = await _context.BoutiqueSaleEntries.AsNoTracking().SingleAsync();
        Assert.Contains("returned the saree", entry.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refunding_ThenConfirmingAgain_DoesNotDoubleCount()
    {
        var (orgId, _, paymentId) = await SeedAsync(status: "confirmed");

        await _payments.RefundPaymentAsync(orgId, paymentId, "Client returned the saree.", ct: default, refundedByUserId: RefundActor);
        // A refunded payment can no longer be confirmed, so the ledger keeps exactly one row.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _payments.ConfirmPaymentAsync(
                orgId, paymentId, new ConfirmPaymentDto { GatewayTransactionId = "txn-late" }));

        Assert.Equal(1, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task RefundingAnUnconfirmedPayment_WritesNoEntry()
    {
        var (orgId, _, paymentId) = await SeedAsync(status: "pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _payments.RefundPaymentAsync(orgId, paymentId, "Should not be possible.", ct: default, refundedByUserId: RefundActor));

        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task ThePaymentEntry_CarriesTheConfirmationTimeAsItsOccurrence()
    {
        var (orgId, _, paymentId) = await SeedAsync();

        await _payments.ConfirmPaymentAsync(
            orgId, paymentId, new ConfirmPaymentDto { GatewayTransactionId = "txn-1" });

        var payment = await _context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId);
        var entry = await _context.BoutiqueSaleEntries.AsNoTracking().SingleAsync();
        // A register ordered by `RecordedAt` would report a payment confirmed yesterday as today's
        // takings, so the occurrence must be the confirmation.
        Assert.NotNull(payment.ConfirmedAt);
        Assert.Equal(payment.ConfirmedAt!.Value, entry.OccurredAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AConfirmedPaymentForAnotherOrganization_WritesNoEntry()
    {
        var mine = await SeedAsync();
        var theirs = await SeedAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _payments.ConfirmPaymentAsync(
                mine.OrgId, theirs.PaymentId, new ConfirmPaymentDto { GatewayTransactionId = "txn" }));

        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }
}
