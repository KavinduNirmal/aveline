using Aveline.Api.Configurations;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
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

        return endpoints;
    }
}
