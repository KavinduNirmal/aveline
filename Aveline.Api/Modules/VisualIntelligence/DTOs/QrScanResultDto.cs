namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// Result envelope returned by the QR scanning and resolution endpoint.
/// </summary>
public class QrScanResultDto
{
    /// <summary>
    /// Whether the scan operation processed without infrastructure error.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Whether the payload matched an active, accessible domain entity in the current tenant.
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// The resolved entity type: "InventoryItem", "GenericPayload", or "Unknown".
    /// </summary>
    public string ScanType { get; set; } = "Unknown";

    /// <summary>
    /// The decoded raw text string extracted from the QR barcode or supplied in request.
    /// </summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary>
    /// The matched identifier (GUID or SKU) if resolved.
    /// </summary>
    public string? MatchedIdentifier { get; set; }

    /// <summary>
    /// The resolved inventory item if the QR code represents a catalog piece.
    /// </summary>
    public InventoryItemDto? Item { get; set; }

    /// <summary>
    /// User-friendly message explaining the resolution status.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
