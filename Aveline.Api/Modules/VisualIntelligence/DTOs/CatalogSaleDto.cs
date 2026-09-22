namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// A counter sale of one catalog piece. The quantity is the only required field; the unit price is
/// optional so a counter can sell at the price the row already carries without restating it, and an
/// explicit price is what an end-of-season or negotiated sale sends.
/// </summary>
public class RecordCatalogSaleDto
{
    public Guid OrganizationId { get; set; }

    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }

    /// <summary>The number of pieces sold. Must be at least one and no more than the stock on hand.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// The price one piece sold for. Omitted means "the catalog price"; supplied means the counter
    /// agreed a different one. Never signed, and never zero: a sale with no money in it is a gift,
    /// and the takings journal refuses a non-positive amount.
    /// </summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>The client the sale is attributed to, when the counter captured one.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Free-text note carried into the takings journal's reason.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// What the counter gets back after a sale: the piece as it now stands, and the money the sale put
/// into the takings journal. <see cref="LedgerEntryId"/> is present so the receipt names the exact
/// row the register will show, rather than asserting that one was written.
/// </summary>
public class CatalogSaleReceiptDto
{
    public Guid ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string? Sku { get; set; }

    public int QuantitySold { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>The stock left after the sale. A real measurement, including a measured zero.</summary>
    public int RemainingStock { get; set; }

    /// <summary>The item's stock-derived status after the sale.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid LedgerEntryId { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}
