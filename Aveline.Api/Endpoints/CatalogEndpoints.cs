using System.IO;
using System.Text;
using Aveline.Api.Configurations;
using Aveline.Api.Common.Media;
using Aveline.Api.Modules.VisualIntelligence;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Aveline.Api.Modules.VisualIntelligence.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Tenant-facing endpoints for the Catalog, Visual Intelligence, Lookbooks, Sourcing, and Suppliers.
/// All routes are scoped under /api/v1/orgs/{organizationId:guid}/catalog and guarded by
/// <see cref="AuthorizationConfiguration.BoutiqueAccessPolicy"/> (which requires a valid Clerk Bearer
/// token, an active organization membership, and the catalog:view permission).
/// </summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orgs/{organizationId:guid}/catalog")
            .WithTags("Catalog & Visual Intelligence")
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueAccessPolicy);

        // --- Inventory & Items ---

        group.MapGet("/items", async (
            [FromRoute] Guid organizationId,
            [FromQuery] string? category,
            [FromQuery] string? color,
            [FromQuery] string? size,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] bool? inStockOnly,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var searchDto = new SearchInventoryDto
            {
                OrganizationId = organizationId,
                Category = category,
                Color = color,
                Size = size,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                InStockOnly = inStockOnly ?? false,
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(50)
            };

            var items = await visualService.SearchInventoryAsync(searchDto, cancellationToken);
            return Results.Ok(items);
        })
        .WithName("CatalogSearchInventory")
        .WithSummary("Search inventory items for the boutique catalog.")
        .Produces<IReadOnlyList<InventoryItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/items/search", async (
            [FromRoute] Guid organizationId,
            [FromBody] SearchInventoryDto request,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            request.OrganizationId = organizationId;
            var items = await visualService.SearchInventoryAsync(request, cancellationToken);
            return Results.Ok(items);
        })
        .WithName("CatalogSearchInventoryPost")
        .WithSummary("Search inventory items via POST request payload.")
        .Produces<IReadOnlyList<InventoryItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/items/{itemId:guid}", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid itemId,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var item = await visualService.GetItemByIdAsync(itemId, organizationId, cancellationToken);
            if (item is null)
            {
                return Results.NotFound(new { error = "Catalog item not found." });
            }

            return Results.Ok(item);
        })
        .WithName("CatalogGetItem")
        .WithSummary("Get a specific catalog item by ID.")
        .Produces<InventoryItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/items", async (
            [FromRoute] Guid organizationId,
            [FromBody] CreateInventoryItemDto dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            dto.OrganizationId = organizationId;
            var created = await visualService.CreateInventoryItemAsync(dto, cancellationToken);
            return Results.Created($"/api/v1/orgs/{organizationId}/catalog/items/{created.Id}", created);
        })
        .WithName("CatalogCreateItem")
        .WithSummary("Create a new item in the boutique inventory catalog.")
        .Produces<InventoryItemDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/items/{itemId:guid}", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid itemId,
            [FromBody] UpdateInventoryItemDto dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            dto.OrganizationId = organizationId;
            var updated = await visualService.UpdateInventoryItemAsync(itemId, dto, cancellationToken);
            if (updated is null)
            {
                return Results.NotFound(new { error = "Catalog item not found." });
            }

            return Results.Ok(updated);
        })
        .WithName("CatalogUpdateItem")
        .WithSummary("Update details of an existing catalog item.")
        .Produces<InventoryItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPatch("/items/{itemId:guid}/status", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid itemId,
            [FromBody] UpdateInventoryStatusDto dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            dto.OrganizationId = organizationId;
            var updated = await visualService.UpdateInventoryStatusAsync(itemId, dto, cancellationToken);
            if (updated is null)
            {
                return Results.NotFound(new { error = "Catalog item not found." });
            }

            return Results.Ok(updated);
        })
        .WithName("CatalogUpdateStatus")
        .WithSummary("Update status (available/reserved/archived) of a catalog item.")
        .Produces<InventoryItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/items/{itemId:guid}", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid itemId,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var deleted = await visualService.DeleteInventoryItemAsync(itemId, organizationId, cancellationToken);
            if (!deleted)
            {
                return Results.NotFound(new { error = "Catalog item not found or already deleted." });
            }

            return Results.NoContent();
        })
        .WithName("CatalogDeleteItem")
        .WithSummary("Delete (soft-delete) an inventory item from the boutique catalog.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/low-stock", async (
            [FromRoute] Guid organizationId,
            [FromQuery] int? threshold,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var items = await visualService.GetLowStockInventoryAsync(organizationId, threshold.GetValueOrDefault(5), cancellationToken);
            return Results.Ok(items);
        })
        .WithName("CatalogGetLowStock")
        .WithSummary("Get inventory items with low stock.")
        .Produces<IReadOnlyList<InventoryItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // --- QR Codes & Scanning ---

        group.MapGet("/items/{itemId:guid}/qr", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid itemId,
            [FromQuery] string? format,
            [FromQuery] int? size,
            [FromServices] IQrCodeService qrService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var requestedFormat = format?.Trim().ToLowerInvariant() ?? "png";
            var requestedSize = Math.Clamp(size.GetValueOrDefault(300), 50, 2000);

            if (requestedFormat == "json")
            {
                try
                {
                    var dto = await qrService.GenerateItemQrDtoAsync(organizationId, itemId, "png", requestedSize, cancellationToken);
                    return Results.Ok(dto);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = "Catalog item not found." });
                }
            }

            try
            {
                var bytes = await qrService.GenerateItemQrBytesAsync(organizationId, itemId, requestedFormat, requestedSize, cancellationToken);
                context.Response.Headers.CacheControl = "public, max-age=86400";
                context.Response.Headers.XContentTypeOptions = "nosniff";

                if (requestedFormat == "svg")
                {
                    return Results.File(bytes, "image/svg+xml; charset=utf-8");
                }

                return Results.File(bytes, "image/png");
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Catalog item not found." });
            }
        })
        .WithName("CatalogGetItemQr")
        .WithSummary("Generate and retrieve a QR code for a specific catalog inventory item.")
        .Produces(StatusCodes.Status200OK)
        .Produces<QrCodeResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/qr/generate", (
            [FromRoute] Guid organizationId,
            [FromBody] GenerateQrDto request,
            [FromServices] IQrCodeService qrService,
            HttpContext context) =>
        {
            var requestedFormat = request.Format?.Trim().ToLowerInvariant() ?? "png";
            if (requestedFormat == "json" || requestedFormat == "base64")
            {
                var result = qrService.GenerateQrResponse(request);
                return Results.Ok(result);
            }

            var size = Math.Clamp(request.Size, 50, 2000);
            context.Response.Headers.CacheControl = "public, max-age=86400";
            context.Response.Headers.XContentTypeOptions = "nosniff";

            if (requestedFormat == "svg")
            {
                var svg = qrService.GenerateSvg(request.Payload, size, request.EccLevel, request.QuietZone);
                return Results.File(Encoding.UTF8.GetBytes(svg), "image/svg+xml; charset=utf-8");
            }

            var pngBytes = qrService.GeneratePng(request.Payload, size, request.EccLevel, request.QuietZone);
            return Results.File(pngBytes, "image/png");
        })
        .WithName("CatalogGenerateQr")
        .WithSummary("Generate a custom QR code (PNG, SVG, or JSON base64 data URL) for any payload.")
        .Produces<QrCodeResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        var scanHandler = async (
            [FromRoute] Guid organizationId,
            HttpRequest request,
            [FromServices] IQrCodeService qrService,
            CancellationToken cancellationToken) =>
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
                if (file != null && file.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, cancellationToken);
                    var scanResult = await qrService.ScanAndResolveImageBytesAsync(organizationId, ms.ToArray(), cancellationToken);
                    return Results.Ok(scanResult);
                }

                var codeFromForm = form["code"].ToString();
                if (string.IsNullOrWhiteSpace(codeFromForm))
                {
                    codeFromForm = form["payload"].ToString();
                }

                if (!string.IsNullOrWhiteSpace(codeFromForm))
                {
                    var scanResult = await qrService.ScanAndResolveAsync(organizationId, new ScanQrDto { Code = codeFromForm }, cancellationToken);
                    return Results.Ok(scanResult);
                }
            }
            else if (request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var payload = await request.ReadFromJsonAsync<ScanQrDto>(cancellationToken);
                if (payload != null)
                {
                    var scanResult = await qrService.ScanAndResolveAsync(organizationId, payload, cancellationToken);
                    return Results.Ok(scanResult);
                }
            }

            return Results.BadRequest(new { error = "Invalid scan request. Provide a 'code', 'payload', 'imageData', or upload a valid QR image file." });
        };

        group.MapPost("/items/scan-qr", scanHandler)
            .WithName("CatalogScanItemQr")
            .WithSummary("Scan and resolve a QR barcode or uploaded camera image to an inventory catalog piece.")
            .Produces<QrScanResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .DisableAntiforgery();

        group.MapPost("/qr/scan", scanHandler)
            .WithName("CatalogScanQrAlias")
            .WithSummary("Alias endpoint for scanning and resolving QR codes.");

        // --- Vision Analysis ---

        group.MapPost("/analyze-image", async (
            [FromRoute] Guid organizationId,
            [FromBody] AnalyzeImageDto dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            dto.OrganizationId = organizationId;
            var analysis = await visualService.AnalyzeImageAsync(dto, cancellationToken);
            return Results.Ok(analysis);
        })
        .WithName("CatalogAnalyzeImage")
        .WithSummary("Extract visual fashion attributes and tags using Elle Vision AI.")
        .Produces<ImageAnalysisResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // --- Customer Matches ---

        group.MapGet("/items/{itemId:guid}/matches", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid itemId,
            [FromQuery] double? minScore,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var matches = await visualService.GetCustomerMatchesAsync(itemId, organizationId, minScore.GetValueOrDefault(0.7), cancellationToken);
            return Results.Ok(matches);
        })
        .WithName("CatalogGetCustomerMatches")
        .WithSummary("Get customers matching a specific catalog item.")
        .Produces<IReadOnlyList<CustomerMatchDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/items/{itemId:guid}/matches/generate", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid itemId,
            [FromBody] GenerateCustomerMatchesDto? dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var request = dto ?? new GenerateCustomerMatchesDto();
            request.OrganizationId = organizationId;
            var matches = await visualService.GenerateCustomerMatchesAsync(itemId, request, cancellationToken);
            return Results.Ok(matches);
        })
        .WithName("CatalogGenerateCustomerMatches")
        .WithSummary("Trigger customer style match computation for a catalog item.")
        .Produces<IReadOnlyList<CustomerMatchDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // --- Lookbooks & Outfit Composition ---

        group.MapGet("/lookbooks", async (
            [FromRoute] Guid organizationId,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var lookbooks = await visualService.GetLookbooksByOrgIdAsync(organizationId, cancellationToken);
            return Results.Ok(lookbooks);
        })
        .WithName("CatalogGetLookbooks")
        .WithSummary("List all composed lookbooks and outfit capsules.")
        .Produces<IReadOnlyList<OutfitCompositionDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/lookbooks/compose", async (
            [FromRoute] Guid organizationId,
            [FromBody] ComposeOutfitDto dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            dto.OrganizationId = organizationId;
            var outfit = await visualService.ComposeOutfitAsync(dto, cancellationToken);
            return Results.Ok(outfit);
        })
        .WithName("CatalogComposeOutfit")
        .WithSummary("Compose a styled outfit look around a primary item.")
        .Produces<ComposedOutfitDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // --- Sourcing Requests ---

        group.MapGet("/sourcing", async (
            [FromRoute] Guid organizationId,
            [FromQuery] string? status,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var requests = await visualService.GetSourcingRequestsByOrgIdAsync(organizationId, status, cancellationToken);
            return Results.Ok(requests);
        })
        .WithName("CatalogGetSourcingRequests")
        .WithSummary("List sourcing request tickets for the boutique.")
        .Produces<IReadOnlyList<SourcingRequestDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/sourcing", async (
            [FromRoute] Guid organizationId,
            [FromBody] CreateSourcingRequestDto dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            dto.OrganizationId = organizationId;
            var created = await visualService.CreateSourcingRequestAsync(dto, cancellationToken);
            return Results.Created($"/api/v1/orgs/{organizationId}/catalog/sourcing/{created.Id}", created);
        })
        .WithName("CatalogCreateSourcingRequest")
        .WithSummary("Create a new sourcing request ticket.")
        .Produces<SourcingRequestDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPatch("/sourcing/{id:guid}/status", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid id,
            [FromBody] UpdateSourcingStatusDto dto,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var updated = await visualService.UpdateSourcingRequestStatusAsync(id, organizationId, dto.Status, cancellationToken);
            if (updated is null)
            {
                return Results.NotFound(new { error = "Sourcing request not found." });
            }

            return Results.Ok(updated);
        })
        .WithName("CatalogUpdateSourcingStatus")
        .WithSummary("Update the status of a sourcing request ticket.")
        .Produces<SourcingRequestDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // --- Suppliers & External Catalogs ---

        group.MapGet("/suppliers", async (
            [FromRoute] Guid organizationId,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var suppliers = await visualService.GetSuppliersByOrgIdAsync(organizationId, cancellationToken);
            return Results.Ok(suppliers);
        })
        .WithName("CatalogGetSuppliers")
        .WithSummary("List integrated suppliers for the boutique.")
        .Produces<IReadOnlyList<SupplierDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/suppliers/{supplierId:guid}/catalog", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid supplierId,
            [FromQuery] string? category,
            [FromQuery] string? color,
            [FromQuery] decimal? maxPrice,
            [FromServices] IVisualService visualService,
            CancellationToken cancellationToken) =>
        {
            var catalogItems = await visualService.GetSupplierCatalogAsync(supplierId, organizationId, category, color, maxPrice, cancellationToken);
            return Results.Ok(catalogItems);
        })
        .WithName("CatalogGetSupplierCatalog")
        .WithSummary("Query an external supplier catalog.")
        .Produces<IReadOnlyList<SupplierCatalogItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // --- Binary Media Storage (PostgreSQL bytea) ---

        group.MapPost("/images/upload", async (
            [FromRoute] Guid organizationId,
            HttpRequest request,
            [FromServices] IInventoryRepository repository,
            CancellationToken cancellationToken) =>
        {
            byte[]? bytes = null;
            string contentType = MediaContentTypes.DefaultImage;
            string? fileName = null;
            long fileSizeBytes = 0;

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
                if (file != null && file.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, cancellationToken);
                    bytes = ms.ToArray();
                    contentType = MediaContentTypes.NormalizeImage(file.ContentType);
                    fileName = file.FileName;
                    fileSizeBytes = file.Length;
                }
            }
            else if (request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var payload = await request.ReadFromJsonAsync<UploadImagePayloadDto>(cancellationToken);
                if (payload != null && !string.IsNullOrWhiteSpace(payload.ImageData))
                {
                    var raw = payload.ImageData.Trim();
                    if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var commaIdx = raw.IndexOf(',');
                        if (commaIdx > 0)
                        {
                            var mimePart = raw[5..commaIdx];
                            if (mimePart.Contains(';'))
                            {
                                contentType = MediaContentTypes.NormalizeImage(mimePart.Split(';')[0]);
                            }
                            bytes = Convert.FromBase64String(raw[(commaIdx + 1)..]);
                        }
                    }
                    else
                    {
                        bytes = Convert.FromBase64String(raw);
                    }
                    fileName = payload.FileName ?? $"upload_{DateTime.UtcNow.Ticks}.jpg";
                    fileSizeBytes = bytes?.Length ?? 0;
                }
            }

            if (bytes == null || bytes.Length == 0)
            {
                return Results.BadRequest(new { error = "No valid image data provided." });
            }

            var imageId = Guid.NewGuid();
            var imageRecord = new InventoryImage
            {
                Id = imageId,
                OrgId = organizationId,
                ImageData = bytes,
                ContentType = contentType,
                FileName = fileName,
                FileSizeBytes = fileSizeBytes,
                ImageUrl = $"/api/v1/orgs/{organizationId}/catalog/images/{imageId}",
                CreatedAtUtc = DateTime.UtcNow
            };

            await repository.AddImageAsync(imageRecord, cancellationToken);

            return Results.Ok(new
            {
                id = imageRecord.Id,
                url = imageRecord.ImageUrl,
                fileName = imageRecord.FileName,
                size = imageRecord.FileSizeBytes,
                contentType = imageRecord.ContentType
            });
        })
        .WithName("CatalogUploadImage")
        .WithSummary("Upload and store an image binary directly in the PostgreSQL database.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .DisableAntiforgery();

        group.MapGet("/images/{imageId:guid}", async (
            [FromRoute] Guid organizationId,
            [FromRoute] Guid imageId,
            [FromServices] IInventoryRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var image = await repository.GetImageByIdAsync(imageId, organizationId, cancellationToken);
            if (image == null || image.ImageData == null || image.ImageData.Length == 0)
            {
                return Results.NotFound(new { error = "Image not found." });
            }

            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            // The stored content type comes from the uploader, so never let the browser
            // sniff or render a non-image payload (e.g. text/html) from this origin.
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(image.ImageData, MediaContentTypes.SafeServe(image.ContentType));
        })
        .WithName("CatalogGetImage")
        .WithSummary("Retrieve physical image binary from PostgreSQL.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AllowAnonymous();

        return endpoints;
    }
}
