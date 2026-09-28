namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// Payload supplied to QR scanning and item resolution endpoints.
/// Supports scanned strings, deep links, SKUs, or base64 visual image data.
/// </summary>
public class ScanQrDto
{
    /// <summary>
    /// Scanned raw text / QR payload / SKU / GUID / URL string.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Alias for Code.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Optional Base64 data URL or raw Base64 image bytes to be visually decoded by the server.
    /// </summary>
    public string? ImageData { get; set; }

    /// <summary>
    /// Returns the effective text payload if supplied via Code or Payload.
    /// </summary>
    public string? GetEffectivePayload()
    {
        if (!string.IsNullOrWhiteSpace(Code)) return Code.Trim();
        if (!string.IsNullOrWhiteSpace(Payload)) return Payload.Trim();
        return null;
    }
}
