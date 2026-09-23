using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using QRCoder;
using ZXing;
using ZXing.Common;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

/// <summary>
/// Production implementation of QR code generation and polymorphic resolution for Aveline boutique catalog.
/// </summary>
public class QrCodeService : IQrCodeService
{
    private readonly IInventoryRepository _inventoryRepository;

    public QrCodeService(IInventoryRepository inventoryRepository)
    {
        _inventoryRepository = inventoryRepository ?? throw new ArgumentNullException(nameof(inventoryRepository));
    }

    public byte[] GeneratePng(string payload, int size = 300, string ecc = "M", int quietZone = 2)
    {
        if (string.IsNullOrEmpty(payload))
        {
            payload = "aveline://empty";
        }

        var eccLevel = ParseEccLevel(ecc);
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, eccLevel);

        int moduleCount = qrCodeData.ModuleMatrix.Count;
        int pixelsPerModule = Math.Max(1, size / Math.Max(moduleCount, 1));
        if (pixelsPerModule < 1) pixelsPerModule = 1;

        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(pixelsPerModule, drawQuietZones: quietZone > 0);
    }

    public string GenerateSvg(string payload, int size = 300, string ecc = "M", int quietZone = 2)
    {
        if (string.IsNullOrEmpty(payload))
        {
            payload = "aveline://empty";
        }

        var eccLevel = ParseEccLevel(ecc);
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, eccLevel);

        int moduleCount = qrCodeData.ModuleMatrix.Count;
        int pixelsPerModule = Math.Max(1, size / Math.Max(moduleCount, 1));
        if (pixelsPerModule < 1) pixelsPerModule = 1;

        var svgCode = new SvgQRCode(qrCodeData);
        return svgCode.GetGraphic(pixelsPerModule, "#000000", "#ffffff", drawQuietZones: quietZone > 0);
    }

    public QrCodeResponseDto GenerateQrResponse(GenerateQrDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var payload = string.IsNullOrWhiteSpace(dto.Payload) ? "aveline://empty" : dto.Payload.Trim();
        var format = string.IsNullOrWhiteSpace(dto.Format) ? "png" : dto.Format.Trim().ToLowerInvariant();
        var size = Math.Clamp(dto.Size, 50, 2000);
        var ecc = string.IsNullOrWhiteSpace(dto.EccLevel) ? "M" : dto.EccLevel.Trim().ToUpperInvariant();
        var quietZone = Math.Max(0, dto.QuietZone);

        var response = new QrCodeResponseDto
        {
            Payload = payload,
            Format = format,
            Size = size,
            EccLevel = ecc,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (format == "svg")
        {
            var svg = GenerateSvg(payload, size, ecc, quietZone);
            response.Svg = svg;
            response.DataUrl = $"data:image/svg+xml;utf8,{Uri.EscapeDataString(svg)}";
        }
        else
        {
            // Default to PNG byte array
            var pngBytes = GeneratePng(payload, size, ecc, quietZone);
            var base64 = Convert.ToBase64String(pngBytes);
            response.Base64 = base64;
            response.DataUrl = $"data:image/png;base64,{base64}";
        }

        return response;
    }

    public async Task<QrCodeResponseDto> GenerateItemQrDtoAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default)
    {
        var item = await _inventoryRepository.GetByIdAsync(itemId, orgId, cancellationToken);
        if (item == null)
        {
            throw new KeyNotFoundException($"Inventory item with ID {itemId} was not found for organization {orgId}.");
        }

        var itemPayload = new ItemQrPayload
        {
            Type = "aveline_inventory_item",
            OrgId = orgId,
            ItemId = itemId,
            Sku = item.Sku,
            // The organisation is carried in the payload and in the URL: a code that names only a
            // piece id cannot say which boutique's catalog it belongs to.
            Url = $"/catalog/items/{itemId}?org={orgId}",
            Version = 1
        };

        var jsonPayload = JsonSerializer.Serialize(itemPayload);
        return GenerateQrResponse(new GenerateQrDto
        {
            Payload = jsonPayload,
            Format = format,
            Size = size,
            EccLevel = "M"
        });
    }

    public async Task<byte[]> GenerateItemQrBytesAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default)
    {
        var item = await _inventoryRepository.GetByIdAsync(itemId, orgId, cancellationToken);
        if (item == null)
        {
            throw new KeyNotFoundException($"Inventory item with ID {itemId} was not found for organization {orgId}.");
        }

        var itemPayload = new ItemQrPayload
        {
            Type = "aveline_inventory_item",
            OrgId = orgId,
            ItemId = itemId,
            Sku = item.Sku,
            // The organisation is carried in the payload and in the URL: a code that names only a
            // piece id cannot say which boutique's catalog it belongs to.
            Url = $"/catalog/items/{itemId}?org={orgId}",
            Version = 1
        };

        var jsonPayload = JsonSerializer.Serialize(itemPayload);
        var normFormat = format?.Trim().ToLowerInvariant();

        if (normFormat == "svg")
        {
            var svg = GenerateSvg(jsonPayload, size);
            return Encoding.UTF8.GetBytes(svg);
        }

        return GeneratePng(jsonPayload, size);
    }

    public string? DecodeQrFromImageBytes(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            // Attempt decoding using ZXing managed luminance
            // 1. If it is raw luminance / bitmap bytes:
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE, BarcodeFormat.CODE_128, BarcodeFormat.EAN_13, BarcodeFormat.EAN_8 }
                }
            };

            // Convert imageBytes into a basic LuminanceSource
            // For standard byte buffer, we sample grayscale luminance
            var luminanceSource = CreateLuminanceSourceFromBytes(imageBytes);
            if (luminanceSource != null)
            {
                var result = reader.Decode(luminanceSource);
                if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                {
                    return result.Text;
                }
            }
        }
        catch
        {
            // Fall through gracefully if decoding unparseable image format
        }

        return null;
    }

    public async Task<QrScanResultDto> ScanAndResolveAsync(
        Guid orgId,
        ScanQrDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? rawPayload = request.GetEffectivePayload();

        // If an image was provided as Base64 data URL, decode it first
        if (string.IsNullOrWhiteSpace(rawPayload) && !string.IsNullOrWhiteSpace(request.ImageData))
        {
            byte[]? imageBytes = ExtractBytesFromDataUrlOrBase64(request.ImageData);
            if (imageBytes != null && imageBytes.Length > 0)
            {
                rawPayload = DecodeQrFromImageBytes(imageBytes);
            }
        }

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return new QrScanResultDto
            {
                Success = false,
                Found = false,
                ScanType = "Unknown",
                RawPayload = string.Empty,
                Message = "No valid QR code or text payload could be extracted from the request."
            };
        }

        return await ResolvePayloadToItemAsync(orgId, rawPayload, cancellationToken);
    }

    public async Task<QrScanResultDto> ScanAndResolveImageBytesAsync(
        Guid orgId,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return new QrScanResultDto
            {
                Success = false,
                Found = false,
                ScanType = "Unknown",
                RawPayload = string.Empty,
                Message = "Empty image binary received."
            };
        }

        var decodedText = DecodeQrFromImageBytes(imageBytes);
        if (string.IsNullOrWhiteSpace(decodedText))
        {
            return new QrScanResultDto
            {
                Success = true,
                Found = false,
                ScanType = "Unknown",
                RawPayload = string.Empty,
                Message = "No readable QR code found in the uploaded image."
            };
        }

        return await ResolvePayloadToItemAsync(orgId, decodedText, cancellationToken);
    }

    private async Task<QrScanResultDto> ResolvePayloadToItemAsync(
        Guid orgId,
        string rawPayload,
        CancellationToken cancellationToken)
    {
        var trimmed = rawPayload.Trim();

        // 1. Check if structured JSON payload
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                var structured = JsonSerializer.Deserialize<ItemQrPayload>(trimmed, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (structured != null && structured.ItemId != Guid.Empty)
                {
                    // Verify tenant boundary
                    if (structured.OrgId != Guid.Empty && structured.OrgId != orgId)
                    {
                        return new QrScanResultDto
                        {
                            Success = true,
                            Found = false,
                            ScanType = "InventoryItem",
                            RawPayload = rawPayload,
                            MatchedIdentifier = structured.ItemId.ToString(),
                            Message = "This piece belongs to a different boutique organization."
                        };
                    }

                    var matchedItem = await _inventoryRepository.GetByIdAsync(structured.ItemId, orgId, cancellationToken);
                    if (matchedItem != null)
                    {
                        return new QrScanResultDto
                        {
                            Success = true,
                            Found = true,
                            ScanType = "InventoryItem",
                            RawPayload = rawPayload,
                            MatchedIdentifier = matchedItem.Id.ToString(),
                            Item = InventoryItemDto.FromDomain(matchedItem),
                            Message = $"Found catalog piece '{matchedItem.ItemName}'."
                        };
                    }
                }
            }
            catch (JsonException)
            {
                // Not a valid ItemQrPayload JSON; proceed to heuristic resolution
            }
        }

        // 2. Check if URL containing an Item GUID (e.g. /catalog/items/{guid} or https://.../items/{guid})
        if (trimmed.Contains("/items/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(new[] { "/items/" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                var potentialGuidStr = parts[1].Split(new[] { '?', '/', '#', '&' }, StringSplitOptions.RemoveEmptyEntries)[0];
                if (Guid.TryParse(potentialGuidStr, out var urlGuid))
                {
                    var itemByUrl = await _inventoryRepository.GetByIdAsync(urlGuid, orgId, cancellationToken);
                    if (itemByUrl != null)
                    {
                        return new QrScanResultDto
                        {
                            Success = true,
                            Found = true,
                            ScanType = "InventoryItem",
                            RawPayload = rawPayload,
                            MatchedIdentifier = itemByUrl.Id.ToString(),
                            Item = InventoryItemDto.FromDomain(itemByUrl),
                            Message = $"Found catalog piece '{itemByUrl.ItemName}' via URL link."
                        };
                    }
                }
            }
        }

        // 3. Check if raw GUID string
        if (Guid.TryParse(trimmed, out var directGuid))
        {
            var itemByGuid = await _inventoryRepository.GetByIdAsync(directGuid, orgId, cancellationToken);
            if (itemByGuid != null)
            {
                return new QrScanResultDto
                {
                    Success = true,
                    Found = true,
                    ScanType = "InventoryItem",
                    RawPayload = rawPayload,
                    MatchedIdentifier = itemByGuid.Id.ToString(),
                    Item = InventoryItemDto.FromDomain(itemByGuid),
                    Message = $"Found catalog piece '{itemByGuid.ItemName}' via Item ID."
                };
            }
        }

        // 4. Check if SKU code
        var itemBySku = await _inventoryRepository.GetBySkuAsync(trimmed, orgId, cancellationToken);
        if (itemBySku != null)
        {
            return new QrScanResultDto
            {
                Success = true,
                Found = true,
                ScanType = "InventoryItem",
                RawPayload = rawPayload,
                MatchedIdentifier = itemBySku.Sku ?? itemBySku.Id.ToString(),
                Item = InventoryItemDto.FromDomain(itemBySku),
                Message = $"Found catalog piece '{itemBySku.ItemName}' matching SKU '{itemBySku.Sku}'."
            };
        }

        // 5. Generic payload (not an inventory item)
        return new QrScanResultDto
        {
            Success = true,
            Found = false,
            ScanType = "GenericPayload",
            RawPayload = rawPayload,
            Message = "QR payload decoded, but no matching catalog piece was found for this boutique."
        };
    }

    private static QRCodeGenerator.ECCLevel ParseEccLevel(string? ecc)
    {
        return ecc?.Trim().ToUpperInvariant() switch
        {
            "L" => QRCodeGenerator.ECCLevel.L,
            "Q" => QRCodeGenerator.ECCLevel.Q,
            "H" => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.M
        };
    }

    private static byte[]? ExtractBytesFromDataUrlOrBase64(string dataUrlOrBase64)
    {
        if (string.IsNullOrWhiteSpace(dataUrlOrBase64)) return null;

        var raw = dataUrlOrBase64.Trim();
        try
        {
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIdx = raw.IndexOf(',');
                if (commaIdx > 0 && commaIdx + 1 < raw.Length)
                {
                    return Convert.FromBase64String(raw[(commaIdx + 1)..]);
                }
            }
            return Convert.FromBase64String(raw);
        }
        catch
        {
            return null;
        }
    }

    private static LuminanceSource? CreateLuminanceSourceFromBytes(byte[] imageBytes)
    {
        if (imageBytes.Length < 8) return null;

        // Simple luminance adapter: for byte buffers, we map bytes into an RGBLuminanceSource
        // If image has standard square or rectangular pixel layout
        int dimension = (int)Math.Sqrt(imageBytes.Length);
        if (dimension > 10)
        {
            byte[] rgb = new byte[dimension * dimension * 3];
            for (int i = 0; i < dimension * dimension && i < imageBytes.Length; i++)
            {
                rgb[i * 3] = imageBytes[i];
                rgb[i * 3 + 1] = imageBytes[i];
                rgb[i * 3 + 2] = imageBytes[i];
            }
            return new RGBLuminanceSource(rgb, dimension, dimension);
        }

        return null;
    }
}
