using Aveline.Api.Configurations;
using Aveline.Application.DTOs.Inventory;
using Aveline.Application.DTOs.Visual;
using Aveline.Application.Services.Visual;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.VisualIntelligence.Controllers;

[ApiController]
[Route("api/internal/visual")]
[Authorize(AuthorizationConfiguration.InternalServicePolicy)]
public class VisualController : ControllerBase
{
    private readonly IVisualService _visualService;

    public VisualController(IVisualService visualService)
    {
        _visualService = visualService ?? throw new ArgumentNullException(nameof(visualService));
    }

    [HttpPost("search-inventory")]
    public async Task<IActionResult> SearchInventory(
        [FromBody] SearchInventoryDto dto)
    {
        var result =
            await _visualService.SearchInventoryAsync(dto);

        return Ok(result);
    }

    [HttpGet("inventory/{itemId:guid}")]
    public async Task<IActionResult> GetInventoryItem(
        [FromRoute] Guid itemId,
        [FromQuery] Guid orgId)
    {
        var item = await _visualService.GetItemByIdAsync(itemId, orgId);
        if (item is null)
        {
            return NotFound(new { error = "Inventory item not found." });
        }

        return Ok(item);
    }

    [HttpPost("inventory")]
    public async Task<IActionResult> CreateInventoryItem(
        [FromBody] CreateInventoryItemDto dto)
    {
        var created = await _visualService.CreateInventoryItemAsync(dto);
        return Created($"/api/internal/visual/inventory/{created.Id}?orgId={created.OrgId}", created);
    }

    [HttpPut("inventory/{itemId:guid}")]
    public async Task<IActionResult> UpdateInventoryItem(
        [FromRoute] Guid itemId,
        [FromBody] UpdateInventoryItemDto dto)
    {
        var updated = await _visualService.UpdateInventoryItemAsync(itemId, dto);
        if (updated is null)
        {
            return NotFound(new { error = "Inventory item not found." });
        }

        return Ok(updated);
    }

    [HttpPatch("inventory/{itemId:guid}/status")]
    public async Task<IActionResult> UpdateInventoryStatus(
        [FromRoute] Guid itemId,
        [FromBody] UpdateInventoryStatusDto dto)
    {
        var updated = await _visualService.UpdateInventoryStatusAsync(itemId, dto);
        if (updated is null)
        {
            return NotFound(new { error = "Inventory item not found." });
        }

        return Ok(updated);
    }

    [HttpGet("inventory/low-stock")]
    public async Task<IActionResult> GetLowStockInventory(
        [FromQuery] Guid orgId,
        [FromQuery] int threshold = 5)
    {
        var items = await _visualService.GetLowStockInventoryAsync(orgId, threshold);
        return Ok(items);
    }

    [HttpPost("analyze-image")]
    public async Task<IActionResult> AnalyzeImage(
        [FromBody] AnalyzeImageDto dto)
    {
        var analysis = await _visualService.AnalyzeImageAsync(dto);
        return Ok(analysis);
    }

    [HttpGet("customer-matches/{itemId:guid}")]
    public async Task<IActionResult> GetCustomerMatches(
        [FromRoute] Guid itemId,
        [FromQuery] Guid orgId,
        [FromQuery] double minScore = 0.7)
    {
        var matches = await _visualService.GetCustomerMatchesAsync(itemId, orgId, minScore);
        return Ok(matches);
    }

    [HttpPost("customer-matches/{itemId:guid}/generate")]
    public async Task<IActionResult> GenerateCustomerMatches(
        [FromRoute] Guid itemId,
        [FromBody] GenerateCustomerMatchesDto dto)
    {
        var matches = await _visualService.GenerateCustomerMatchesAsync(itemId, dto);
        return Ok(matches);
    }

    [HttpPost("outfits/compose")]
    public async Task<IActionResult> ComposeOutfit(
        [FromBody] ComposeOutfitDto dto)
    {
        var outfit = await _visualService.ComposeOutfitAsync(dto);
        return Ok(outfit);
    }

    [HttpPost("sourcing-requests")]
    public async Task<IActionResult> CreateSourcingRequest(
        [FromBody] CreateSourcingRequestDto dto)
    {
        var request = await _visualService.CreateSourcingRequestAsync(dto);
        return Created($"/api/internal/visual/sourcing-requests/{request.Id}?orgId={request.OrgId}", request);
    }

    [HttpGet("suppliers/{supplierId:guid}/catalog")]
    public async Task<IActionResult> GetSupplierCatalog(
        [FromRoute] Guid supplierId,
        [FromQuery] Guid orgId,
        [FromQuery] string? category = null,
        [FromQuery] string? color = null,
        [FromQuery] decimal? maxPrice = null)
    {
        var items = await _visualService.GetSupplierCatalogAsync(supplierId, orgId, category, color, maxPrice);
        return Ok(items);
    }
}
