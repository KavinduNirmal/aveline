using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// Raised when a sale asks for more pieces than the catalog row has on hand. It is a typed refusal
/// rather than a validation message because the caller must map it to a conflict: the request was
/// well-formed, the shop's state is what says no.
/// </summary>
public sealed class InsufficientStockException(int available, int requested)
    : Exception($"Only {available} piece(s) are in stock; {requested} were requested.")
{
    public int Available { get; } = available;

    public int Requested { get; } = requested;
}

/// <summary>
/// Sells one catalog piece over the counter: it decrements the row's stock, derives its new stock
/// status, and appends the money to the boutique's takings journal in one call so the catalog and
/// the register cannot disagree about whether a sale happened.
/// </summary>
public interface ICatalogSaleService
{
    /// <summary>
    /// Records the sale. Returns <c>null</c> when the id does not name a piece in this organisation
    /// (the caller answers 404), and throws <see cref="InsufficientStockException"/> when the stock
    /// on hand cannot cover the request (the caller answers 409).
    /// </summary>
    Task<CatalogSaleReceiptDto?> RecordSaleAsync(
        Guid organizationId,
        Guid itemId,
        RecordCatalogSaleDto request,
        Guid? recordedByUserId,
        CancellationToken cancellationToken = default);
}

public sealed class CatalogSaleService(
    AppDbContext db,
    IBoutiqueSaleLedgerService ledger,
    ILogger<CatalogSaleService> logger) : ICatalogSaleService
{
    /// <summary>
    /// The stock-derived status the catalog UI already uses (AddProductModal derives the same three
    /// states). Kept here as one expression so the sale path and a manual edit cannot drift.
    /// </summary>
    private static string StatusForStock(int quantity) =>
        quantity <= 0 ? "reserved" : quantity <= 2 ? "low_stock" : "available";

    public async Task<CatalogSaleReceiptDto?> RecordSaleAsync(
        Guid organizationId,
        Guid itemId,
        RecordCatalogSaleDto request,
        Guid? recordedByUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Untracked: the row is read for its identity, name and price. The decrement itself is the
        // guarded UPDATE (or the tracked mutation on the in-memory provider) below, so this read
        // cannot be the thing that loses a concurrent sale.
        var item = await db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == itemId
                             && candidate.OrgId == organizationId
                             && candidate.DeletedAt == null,
                cancellationToken);

        if (item is null)
        {
            return null;
        }

        var quantity = request.Quantity;
        if (quantity <= 0)
        {
            throw new ArgumentException("A sale must sell at least one piece.");
        }

        var unitPrice = request.UnitPrice ?? item.Price;
        if (unitPrice <= 0m)
        {
            // A zero-amount sale is a gift, and the takings journal's check constraint refuses a
            // non-positive amount. Refused here rather than allowed to become a 500 there.
            throw new ArgumentException("A sale must have a positive unit price.");
        }

        if (quantity > item.StockQuantity)
        {
            throw new InsufficientStockException(item.StockQuantity, quantity);
        }

        if (item.Status == "archived")
        {
            // Archived is a deliberate retirement, not a stock level. Selling one would quietly
            // un-retire it, so the counter is told to restore it first.
            throw new ArgumentException("An archived piece cannot be sold. Restore it to the catalog first.");
        }

        var remaining = item.StockQuantity - quantity;
        var status = StatusForStock(remaining);
        var now = DateTime.UtcNow;
        var total = decimal.Round(unitPrice * quantity, 2, MidpointRounding.AwayFromZero);

        // A relational provider takes one guarded UPDATE: two counters selling the last piece at the
        // same instant cannot both succeed, because the second's `StockQuantity >= quantity` clause
        // matches no row. The in-memory provider has no ExecuteUpdate, so tests take the tracked
        // path; the SQL above is what proves the oversell guard.
        if (db.Database.IsRelational())
        {
            var affected = await db.InventoryItems
                .Where(candidate => candidate.Id == itemId
                                    && candidate.OrgId == organizationId
                                    && candidate.DeletedAt == null
                                    && candidate.StockQuantity >= quantity)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.StockQuantity, candidate => candidate.StockQuantity - quantity)
                        .SetProperty(candidate => candidate.Status, status)
                        .SetProperty(candidate => candidate.UpdatedAtUtc, now),
                    cancellationToken);

            if (affected == 0)
            {
                // The stock moved between the read and the update. Re-read to report what is
                // actually left, so the counter is told the truth rather than "conflict".
                var current = await db.InventoryItems
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == itemId && candidate.OrgId == organizationId)
                    .Select(candidate => new { candidate.StockQuantity })
                    .FirstOrDefaultAsync(cancellationToken);
                throw new InsufficientStockException(current?.StockQuantity ?? 0, quantity);
            }
        }
        else
        {
            var tracked = await db.InventoryItems
                .FirstAsync(
                    candidate => candidate.Id == itemId && candidate.OrgId == organizationId,
                    cancellationToken);

            if (tracked.StockQuantity < quantity)
            {
                throw new InsufficientStockException(tracked.StockQuantity, quantity);
            }

            tracked.StockQuantity -= quantity;
            tracked.Status = status;
            tracked.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        var reason = BuildReason(item.ItemName, quantity, request.Note);
        var entry = await ledger.RecordAsync(
            new RecordBoutiqueSaleCommand(
                OrganizationId: organizationId,
                Amount: total,
                Reason: reason,
                Kind: BoutiqueSaleEntryKind.Sale,
                // A person at the counter asserted this amount, so it is money taken rather than a
                // value an order merely implies — the same basis the customer-interaction sale uses.
                ChargeBasis: BoutiqueSaleChargeBasis.Verified,
                SourceKind: BoutiqueSaleSourceKind.CounterWalkIn,
                // The catalog row and moment make the reference unique, so a retry under a fresh
                // request still gets its own row rather than colliding with the previous sale.
                SourceRef: $"catalog-sale:{itemId}:{Guid.NewGuid():N}",
                OccurredAt: now,
                RecordedByUserId: recordedByUserId,
                CustomerId: request.CustomerId),
            cancellationToken);

        logger.LogInformation(
            "Catalog sale recorded. item={ItemId} org={OrganizationId} quantity={Quantity} total={Total}",
            itemId, organizationId, quantity, total);

        return new CatalogSaleReceiptDto
        {
            ItemId = item.Id,
            ItemName = item.ItemName,
            Sku = item.Sku,
            QuantitySold = quantity,
            UnitPrice = unitPrice,
            TotalAmount = total,
            RemainingStock = remaining,
            Status = status,
            LedgerEntryId = entry.Id,
            RecordedAtUtc = now
        };
    }

    /// <summary>
    /// The journal row's reason. Bounded to the column's 500 characters and never shorter than the
    /// ledger's 10-character floor, which the piece's own name guarantees.
    /// </summary>
    private static string BuildReason(string itemName, int quantity, string? note)
    {
        var reason = $"Counter sale of {quantity} × {itemName}.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            reason = $"{reason} {note.Trim()}";
        }

        return reason.Length <= 500 ? reason : reason[..500];
    }
}
