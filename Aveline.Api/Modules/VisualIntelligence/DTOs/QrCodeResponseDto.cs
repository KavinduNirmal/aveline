using System;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// Structured JSON envelope returned when generating a QR code in JSON/Base64/SVG formats.
/// </summary>
public class QrCodeResponseDto
{
    /// <summary>
    /// The decoded payload string encoded within the QR matrix.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Requested format: "png", "svg", "base64", or "json".
    /// </summary>
    public string Format { get; set; } = "png";

    /// <summary>
    /// Browser-renderable Data URL (e.g., "data:image/png;base64,..." or "data:image/svg+xml;utf8,...").
    /// </summary>
    public string? DataUrl { get; set; }

    /// <summary>
    /// Raw Base64 string of the binary PNG image.
    /// </summary>
    public string? Base64 { get; set; }

    /// <summary>
    /// Raw SVG markup string if format is SVG.
    /// </summary>
    public string? Svg { get; set; }

    /// <summary>
    /// Dimension in pixels.
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// Applied error correction level.
    /// </summary>
    public string EccLevel { get; set; } = "M";

    /// <summary>
    /// UTC timestamp of generation.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
