using Aveline.Api.Configurations;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Internal (service-to-service) endpoints for the Visual Intelligence &amp; Sourcing Agent (Slice 2).
/// All routes require the <c>X-Internal-Token</c> header (ADR-009, "InternalServicePolicy") and
/// are consumed by the Python agent service only — never exposed to boutique end-users. The
/// org is carried explicitly in each request/route for tenant scoping.
/// </summary>
public static class VisualEndpoints
{
    public static IEndpointRouteBuilder MapVisualEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Primary route group: /internal/visual
        var group = endpoints.MapGroup("/internal/visual")
            .WithTags("Visual Intelligence & Sourcing (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        group.MapPost("/inventory/search", SearchInventoryAsync)
            .WithName("SearchInventory")
            .WithSummary("Search inventory items by criteria for the visual agent.")
            .Produces<IReadOnlyList<InventoryItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/search-inventory", SearchInventoryAsync)
            .WithName("SearchInventoryAlias");

        group.MapGet("/inventory/{itemId:guid}", GetInventoryItemAsync)
            .WithName("GetInventoryItem")
            .WithSummary("Get a specific inventory item by ID.")
            .Produces<InventoryItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/inventory", CreateInventoryItemAsync)
            .WithName("CreateInventoryItem")
            .WithSummary("Create a new inventory item.")
            .Produces<InventoryItemDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/inventory/{itemId:guid}", UpdateInventoryItemAsync)
            .WithName("UpdateInventoryItem")
            .WithSummary("Update details of an existing inventory item.")
            .Produces<InventoryItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPatch("/inventory/{itemId:guid}/status", UpdateInventoryStatusAsync)
            .WithName("UpdateInventoryStatus")
            .WithSummary("Update status (available/reserved/archived) of an inventory item.")
            .Produces<InventoryItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/inventory/low-stock", GetLowStockInventoryAsync)
            .WithName("GetLowStockInventory")
            .WithSummary("Get items with stock at or below the threshold.")
            .Produces<IReadOnlyList<InventoryItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/analyze-image", AnalyzeImageAsync)
            .WithName("AnalyzeImage")
            .WithSummary("Extract visual attributes from a product image URL.")
            .Produces<ImageAnalysisResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/customer-matches/{itemId:guid}", GetCustomerMatchesAsync)
            .WithName("GetCustomerMatches")
            .WithSummary("Get customers matching a specific inventory item.")
            .Produces<IReadOnlyList<CustomerMatchDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/customer-matches/{itemId:guid}/generate", GenerateCustomerMatchesAsync)
            .WithName("GenerateCustomerMatches")
            .WithSummary("Trigger customer matching computation for a new item.")
            .Produces<IReadOnlyList<CustomerMatchDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/outfits/compose", ComposeOutfitAsync)
            .WithName("ComposeOutfit")
            .WithSummary("Compose a styled outfit look around a primary item.")
            .Produces<ComposedOutfitDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/sourcing-requests", CreateSourcingRequestAsync)
            .WithName("CreateSourcingRequest")
            .WithSummary("Create a new sourcing request ticket.")
            .Produces<SourcingRequestDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/suppliers/{supplierId:guid}/catalog", GetSupplierCatalogAsync)
            .WithName("GetSupplierCatalog")
            .WithSummary("Query an external supplier catalog.")
            .Produces<IReadOnlyList<SupplierCatalogItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        // Backward compatibility aliases (/api/internal/visual/...)
        var apiGroup = endpoints.MapGroup("/api/internal/visual")
            .WithTags("Visual Intelligence & Sourcing (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        apiGroup.MapPost("/inventory/search", SearchInventoryAsync);
        apiGroup.MapPost("/search-inventory", SearchInventoryAsync);
        apiGroup.MapGet("/inventory/{itemId:guid}", GetInventoryItemAsync);
        apiGroup.MapPost("/inventory", CreateInventoryItemAsync);
        apiGroup.MapPut("/inventory/{itemId:guid}", UpdateInventoryItemAsync);
        apiGroup.MapPatch("/inventory/{itemId:guid}/status", UpdateInventoryStatusAsync);
        apiGroup.MapGet("/inventory/low-stock", GetLowStockInventoryAsync);
        apiGroup.MapPost("/analyze-image", AnalyzeImageAsync);
        apiGroup.MapGet("/customer-matches/{itemId:guid}", GetCustomerMatchesAsync);
        apiGroup.MapPost("/customer-matches/{itemId:guid}/generate", GenerateCustomerMatchesAsync);
        apiGroup.MapPost("/outfits/compose", ComposeOutfitAsync);
        apiGroup.MapPost("/sourcing-requests", CreateSourcingRequestAsync);
        apiGroup.MapGet("/suppliers/{supplierId:guid}/catalog", GetSupplierCatalogAsync);

        // Backward compatibility aliases (/internal/inventory/...)
        var inventoryGroup = endpoints.MapGroup("/internal/inventory")
            .WithTags("Visual Intelligence & Sourcing (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        inventoryGroup.MapPost("/search", SearchInventoryAsync);
        inventoryGroup.MapGet("/{itemId:guid}", GetInventoryItemAsync);
        inventoryGroup.MapPost("/", CreateInventoryItemAsync);
        inventoryGroup.MapPut("/{itemId:guid}", UpdateInventoryItemAsync);
        inventoryGroup.MapPatch("/{itemId:guid}/status", UpdateInventoryStatusAsync);
        inventoryGroup.MapGet("/low-stock", GetLowStockInventoryAsync);

        return endpoints;
    }

    private static async Task<IResult> SearchInventoryAsync(
        [FromBody] SearchInventoryDto request,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        if (request == null || (request.OrganizationId == Guid.Empty && request.OrgId == Guid.Empty))
        {
            return Results.BadRequest(new { error = "Valid OrganizationId is required." });
        }

        var results = await visualService.SearchInventoryAsync(request, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GetInventoryItemAsync(
        [FromRoute] Guid itemId,
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? orgId,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var targetOrgId = organizationId ?? orgId ?? Guid.Empty;
        var item = await visualService.GetItemByIdAsync(itemId, targetOrgId, cancellationToken);
        if (item is null)
        {
            return Results.NotFound(new { error = "Inventory item not found." });
        }

        return Results.Ok(item);
    }

    private static async Task<IResult> CreateInventoryItemAsync(
        [FromBody] CreateInventoryItemDto dto,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        if (dto == null || (dto.OrganizationId == Guid.Empty && dto.OrgId == Guid.Empty))
        {
            return Results.BadRequest(new { error = "Valid OrganizationId is required." });
        }

        var created = await visualService.CreateInventoryItemAsync(dto, cancellationToken);
        return Results.Created($"/internal/visual/inventory/{created.Id}?organizationId={created.OrganizationId}", created);
    }

    private static async Task<IResult> UpdateInventoryItemAsync(
        [FromRoute] Guid itemId,
        [FromBody] UpdateInventoryItemDto dto,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var updated = await visualService.UpdateInventoryItemAsync(itemId, dto, cancellationToken);
        if (updated is null)
        {
            return Results.NotFound(new { error = "Inventory item not found." });
        }

        return Results.Ok(updated);
    }

    private static async Task<IResult> UpdateInventoryStatusAsync(
        [FromRoute] Guid itemId,
        [FromBody] UpdateInventoryStatusDto dto,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var updated = await visualService.UpdateInventoryStatusAsync(itemId, dto, cancellationToken);
        if (updated is null)
        {
            return Results.NotFound(new { error = "Inventory item not found." });
        }

        return Results.Ok(updated);
    }

    private static async Task<IResult> GetLowStockInventoryAsync(
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? orgId,
        [FromQuery] int threshold,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var targetOrgId = organizationId ?? orgId ?? Guid.Empty;
        var items = await visualService.GetLowStockInventoryAsync(targetOrgId, threshold > 0 ? threshold : 5, cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> AnalyzeImageAsync(
        [FromBody] AnalyzeImageDto dto,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var analysis = await visualService.AnalyzeImageAsync(dto, cancellationToken);
        return Results.Ok(analysis);
    }

    private static async Task<IResult> GetCustomerMatchesAsync(
        [FromRoute] Guid itemId,
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? orgId,
        [FromQuery] double minScore,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var targetOrgId = organizationId ?? orgId ?? Guid.Empty;
        var matches = await visualService.GetCustomerMatchesAsync(itemId, targetOrgId, minScore > 0 ? minScore : 0.7, cancellationToken);
        return Results.Ok(matches);
    }

    private static async Task<IResult> GenerateCustomerMatchesAsync(
        [FromRoute] Guid itemId,
        [FromBody] GenerateCustomerMatchesDto dto,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var matches = await visualService.GenerateCustomerMatchesAsync(itemId, dto, cancellationToken);
        return Results.Ok(matches);
    }

    private static async Task<IResult> ComposeOutfitAsync(
        [FromBody] ComposeOutfitDto dto,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var outfit = await visualService.ComposeOutfitAsync(dto, cancellationToken);
        return Results.Ok(outfit);
    }

    private static async Task<IResult> CreateSourcingRequestAsync(
        [FromBody] CreateSourcingRequestDto dto,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var request = await visualService.CreateSourcingRequestAsync(dto, cancellationToken);
        return Results.Created($"/internal/visual/sourcing-requests/{request.Id}?organizationId={request.OrganizationId}", request);
    }

    private static async Task<IResult> GetSupplierCatalogAsync(
        [FromRoute] Guid supplierId,
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? orgId,
        [FromQuery] string? category,
        [FromQuery] string? color,
        [FromQuery] decimal? maxPrice,
        [FromServices] IVisualService visualService,
        CancellationToken cancellationToken)
    {
        var targetOrgId = organizationId ?? orgId ?? Guid.Empty;
        var items = await visualService.GetSupplierCatalogAsync(supplierId, targetOrgId, category, color, maxPrice, cancellationToken);
        return Results.Ok(items);
    }
}
