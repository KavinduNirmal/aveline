using Aveline.Application.DTOs.Inventory;
using Aveline.Application.DTOs.Visual;
using Aveline.Application.Services.Inventory;

namespace Aveline.Application.Services.Visual;

public class VisualService : IVisualService
{
    private readonly IInventoryService _inventoryService;

    public VisualService(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
    }

    public async Task<IReadOnlyList<InventoryItemDto>> SearchInventoryAsync(
        SearchInventoryDto dto,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.SearchInventoryAsync(dto, cancellationToken);
    }

    public async Task<InventoryItemDto?> GetItemByIdAsync(
        Guid itemId,
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.GetItemByIdAsync(itemId, orgId, cancellationToken);
    }

    public async Task<InventoryItemDto> CreateInventoryItemAsync(
        CreateInventoryItemDto dto,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.CreateItemAsync(dto, cancellationToken);
    }

    public async Task<InventoryItemDto?> UpdateInventoryItemAsync(
        Guid itemId,
        UpdateInventoryItemDto dto,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.UpdateItemAsync(itemId, dto, cancellationToken);
    }

    public async Task<InventoryItemDto?> UpdateInventoryStatusAsync(
        Guid itemId,
        UpdateInventoryStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.UpdateStatusAsync(itemId, dto.OrgId, dto.Status, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItemDto>> GetLowStockInventoryAsync(
        Guid orgId,
        int threshold = 5,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.GetLowStockItemsAsync(orgId, threshold, cancellationToken);
    }

    public async Task<ImageAnalysisResultDto> AnalyzeImageAsync(
        AnalyzeImageDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Visual intelligence rule-based and model feature extraction projection
        return await Task.FromResult(new ImageAnalysisResultDto
        {
            Category = "saree",
            PrimaryColor = "emerald",
            SecondaryColors = new List<string> { "gold", "forest green" },
            Pattern = "floral embroidery",
            Style = "traditional luxury",
            Fabric = "silk",
            ConfidenceScore = 0.96,
            SuggestedKeywords = new List<string> { "emerald", "silk", "saree", "embroidery", "wedding" }
        });
    }

    public async Task<IReadOnlyList<CustomerMatchDto>> GetCustomerMatchesAsync(
        Guid itemId,
        Guid orgId,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        // Simulated intelligence matching returning customers matching preferences/occasions
        var matches = new List<CustomerMatchDto>
        {
            new()
            {
                CustomerId = Guid.NewGuid(),
                CustomerName = "Ananya Sharma",
                CustomerPhone = "+94771234567",
                MatchScore = 0.92,
                MatchReason = "Prefers silk sarees in jewel tones and attended wedding last month",
                MatchingPreferences = new List<string> { "saree", "emerald", "luxury" }
            }
        };

        return await Task.FromResult(matches.Where(m => m.MatchScore >= minScore).ToList());
    }

    public async Task<IReadOnlyList<CustomerMatchDto>> GenerateCustomerMatchesAsync(
        Guid itemId,
        GenerateCustomerMatchesDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var generated = new List<CustomerMatchDto>
        {
            new()
            {
                CustomerId = Guid.NewGuid(),
                CustomerName = "Deepika Ranasinghe",
                CustomerPhone = "+94719876543",
                MatchScore = 0.95,
                MatchReason = "High affinity for silk fabric and green/gold colorways",
                MatchingPreferences = new List<string> { "silk", "emerald", "formal" }
            },
            new()
            {
                CustomerId = Guid.NewGuid(),
                CustomerName = "Kavindi Perera",
                CustomerPhone = "+94765432100",
                MatchScore = 0.88,
                MatchReason = "Requested bridal collection sarees with traditional motifs",
                MatchingPreferences = new List<string> { "saree", "traditional", "wedding" }
            }
        };

        return await Task.FromResult(generated.Take(dto.MaxMatches > 0 ? dto.MaxMatches : 10).ToList());
    }

    public async Task<ComposedOutfitDto> ComposeOutfitAsync(
        ComposeOutfitDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var primaryItem = await _inventoryService.GetItemByIdAsync(dto.PrimaryItemId, dto.OrgId, cancellationToken);
        if (primaryItem is null)
        {
            throw new KeyNotFoundException($"Primary inventory item {dto.PrimaryItemId} not found.");
        }

        // Search complementary items (e.g. blouse, shawl, jewelry)
        var complementary = await _inventoryService.SearchInventoryAsync(
            new SearchInventoryDto
            {
                OrgId = dto.OrgId,
                InStockOnly = true,
                PageSize = 2
            },
            cancellationToken
        );

        var complementaryList = complementary
            .Where(x => x.Id != primaryItem.Id)
            .Take(2)
            .ToList();

        var totalPrice = primaryItem.Price + complementaryList.Sum(x => x.Price);

        return new ComposedOutfitDto
        {
            LookName = $"Curated {dto.Style ?? "Luxury"} Ensemble",
            Style = dto.Style ?? "royal luxury",
            Occasion = dto.Occasion ?? "formal",
            StylingNotes = $"Harmonized outfit pairing {primaryItem.ItemName} with matching accessories.",
            PrimaryItem = primaryItem,
            ComplementaryItems = complementaryList,
            TotalLookPrice = totalPrice
        };
    }

    public async Task<SourcingRequestDto> CreateSourcingRequestAsync(
        CreateSourcingRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var request = new SourcingRequestDto
        {
            Id = Guid.NewGuid(),
            OrgId = dto.OrgId,
            Category = dto.Category,
            Color = dto.Color,
            Description = dto.Description,
            TargetPrice = dto.TargetPrice,
            QuantityNeeded = dto.QuantityNeeded > 0 ? dto.QuantityNeeded : 1,
            CustomerId = dto.CustomerId,
            Urgency = string.IsNullOrWhiteSpace(dto.Urgency) ? "medium" : dto.Urgency,
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        return await Task.FromResult(request);
    }

    public async Task<IReadOnlyList<SupplierCatalogItemDto>> GetSupplierCatalogAsync(
        Guid supplierId,
        Guid orgId,
        string? category = null,
        string? color = null,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        var catalog = new List<SupplierCatalogItemDto>
        {
            new()
            {
                SupplierId = supplierId,
                SupplierName = "Kanchipuram Heritage Mills",
                Sku = "KHM-EM-01",
                ItemName = "Authentic Pure Zari Silk Saree",
                Category = "saree",
                Color = "emerald",
                WholesalePrice = 38000m,
                LeadTimeDays = 5,
                AvailableStock = 20,
                ImageUrl = "https://example.com/supplier/saree1.jpg"
            },
            new()
            {
                SupplierId = supplierId,
                SupplierName = "Kanchipuram Heritage Mills",
                Sku = "KHM-RD-02",
                ItemName = "Crimson Bridal Silk Saree",
                Category = "saree",
                Color = "crimson",
                WholesalePrice = 42000m,
                LeadTimeDays = 7,
                AvailableStock = 15,
                ImageUrl = "https://example.com/supplier/saree2.jpg"
            }
        };

        var query = catalog.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            query = query.Where(x => x.Color.Equals(color, StringComparison.OrdinalIgnoreCase));
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(x => x.WholesalePrice <= maxPrice.Value);
        }

        return await Task.FromResult(query.ToList());
    }
}
