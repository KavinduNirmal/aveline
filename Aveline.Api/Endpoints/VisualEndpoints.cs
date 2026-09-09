using Aveline.Api.Configurations;
using Aveline.Application.DTOs.Inventory;
using Aveline.Application.Services.Inventory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

public static class VisualEndpoints
{
    public static IEndpointRouteBuilder MapVisualEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/internal/visual")
            .WithTags("Visual Intelligence & Sourcing (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        group.MapPost("/search-inventory", SearchInventoryAsync)
            .WithName("SearchInventory")
            .WithSummary("Search inventory items by criteria for the visual agent.")
            .Produces<IReadOnlyList<InventoryItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> SearchInventoryAsync(
        [FromBody] SearchInventoryDto request,
        [FromServices] IInventoryService inventoryService,
        CancellationToken cancellationToken)
    {
        if (request == null || request.OrgId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Valid OrgId is required." });
        }

        var results = await inventoryService.SearchInventoryAsync(request, cancellationToken);
        return Results.Ok(results);
    }
}
