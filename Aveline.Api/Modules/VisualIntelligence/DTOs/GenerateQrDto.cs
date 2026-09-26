namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// Parameters for generating a custom or item QR code.
/// </summary>
public class GenerateQrDto
{
    /// <summary>
    /// Content string to encode into the QR matrix (URL, JSON, deep link, SKU, etc.).
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Output representation: "png" (binary image), "svg" (vector XML), "json", or "base64".
    /// </summary>
    public string Format { get; set; } = "png";

    /// <summary>
    /// Pixel dimension or viewport size for the rendered QR code (bounded between 50 and 2000).
    /// </summary>
    public int Size { get; set; } = 300;

    /// <summary>
    /// Error correction capability level: "L" (~7%), "M" (~15%), "Q" (~25%), "H" (~30%). Default is "M".
    /// </summary>
    public string EccLevel { get; set; } = "M";

    /// <summary>
    /// Margin / quiet zone border in modules (default is 2).
    /// </summary>
    public int QuietZone { get; set; } = 2;
}
