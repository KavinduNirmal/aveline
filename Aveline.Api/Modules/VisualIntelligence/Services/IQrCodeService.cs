using System;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.DTOs;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

/// <summary>
/// Domain service for generating vector/raster QR codes, encoding item payloads,
/// decoding camera/uploaded images, and resolving scanned QR codes against the boutique catalog.
/// </summary>
public interface IQrCodeService
{
    /// <summary>
    /// Renders a raster PNG byte array for any payload string.
    /// </summary>
    byte[] GeneratePng(string payload, int size = 300, string ecc = "M", int quietZone = 2);

    /// <summary>
    /// Renders a scalable vector SVG string for any payload string.
    /// </summary>
    string GenerateSvg(string payload, int size = 300, string ecc = "M", int quietZone = 2);

    /// <summary>
    /// Generates a structured QR code response envelope (supporting PNG, SVG, Base64, JSON).
    /// </summary>
    QrCodeResponseDto GenerateQrResponse(GenerateQrDto dto);

    /// <summary>
    /// Generates structured QR code metadata and Data URL for an inventory catalog piece.
    /// </summary>
    Task<QrCodeResponseDto> GenerateItemQrDtoAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates binary bytes (PNG or UTF8 SVG) for an inventory item's floor tag QR code.
    /// </summary>
    Task<byte[]> GenerateItemQrBytesAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Visually decodes a 2D QR matrix from raw image bytes (PNG, JPEG, WebP, etc.).
    /// </summary>
    string? DecodeQrFromImageBytes(byte[] imageBytes);

    /// <summary>
    /// Resolves a scan request (payload string, URL, SKU, GUID, or Base64 image) against the boutique catalog.
    /// </summary>
    Task<QrScanResultDto> ScanAndResolveAsync(
        Guid orgId,
        ScanQrDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes image bytes and resolves the contained QR code against the boutique catalog.
    /// </summary>
    Task<QrScanResultDto> ScanAndResolveImageBytesAsync(
        Guid orgId,
        byte[] imageBytes,
        CancellationToken cancellationToken = default);
}
