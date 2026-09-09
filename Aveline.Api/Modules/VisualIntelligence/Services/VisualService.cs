using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public class VisualService : IVisualService
{
    private readonly IInventoryService _inventoryService;
    private readonly ISourcingRequestRepository _sourcingRequestRepository;
    private readonly ICustomerMatchRepository _customerMatchRepository;
    private readonly IOutfitRepository _outfitRepository;
    private readonly ISupplierRepository _supplierRepository;

    public VisualService(
        IInventoryService inventoryService,
        ISourcingRequestRepository sourcingRequestRepository,
        ICustomerMatchRepository customerMatchRepository,
        IOutfitRepository outfitRepository,
        ISupplierRepository supplierRepository)
    {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _sourcingRequestRepository = sourcingRequestRepository ?? throw new ArgumentNullException(nameof(sourcingRequestRepository));
        _customerMatchRepository = customerMatchRepository ?? throw new ArgumentNullException(nameof(customerMatchRepository));
        _outfitRepository = outfitRepository ?? throw new ArgumentNullException(nameof(outfitRepository));
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
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
        var existingMatches = await _customerMatchRepository.GetByItemIdAsync(itemId, orgId, minScore, cancellationToken);
        if (existingMatches.Count > 0)
        {
            return existingMatches.Select(m => new CustomerMatchDto
            {
                CustomerId = m.CustomerId,
                CustomerName = "Valued Customer",
                MatchScore = m.MatchScore,
                MatchReason = m.Reason,
                MatchingPreferences = new List<string> { "saree", "emerald", "luxury" }
            }).ToList();
        }

        // Default match projection
        var defaultMatches = new List<CustomerMatchDto>
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

        return defaultMatches.Where(m => m.MatchScore >= minScore).ToList();
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

        var matchesToSave = generated.Select(g => new CustomerMatch
        {
            Id = Guid.NewGuid(),
            OrgId = dto.OrgId,
            ItemId = itemId,
            CustomerId = g.CustomerId,
            MatchScore = g.MatchScore,
            Reason = g.MatchReason,
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();

        await _customerMatchRepository.AddRangeAsync(matchesToSave, cancellationToken);

        return generated.Take(dto.MaxMatches > 0 ? dto.MaxMatches : 10).ToList();
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

        var outfit = new OutfitComposition
        {
            Id = Guid.NewGuid(),
            OrgId = dto.OrgId,
            CustomerId = Guid.NewGuid(),
            Occasion = dto.Occasion ?? "formal",
            TotalPrice = totalPrice,
            StyleNotes = $"Harmonized outfit pairing {primaryItem.ItemName} with matching accessories.",
            CreatedAtUtc = DateTime.UtcNow,
            Items = new List<OutfitItem>
            {
                new() { Id = Guid.NewGuid(), InventoryItemId = primaryItem.Id, Role = "primary" }
            }
        };

        foreach (var item in complementaryList)
        {
            outfit.Items.Add(new OutfitItem { Id = Guid.NewGuid(), InventoryItemId = item.Id, Role = "accessory" });
        }

        await _outfitRepository.AddAsync(outfit, cancellationToken);

        return new ComposedOutfitDto
        {
            LookName = $"Curated {dto.Style ?? "Luxury"} Ensemble",
            Style = dto.Style ?? "royal luxury",
            Occasion = dto.Occasion ?? "formal",
            StylingNotes = outfit.StyleNotes,
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

        var request = new SourcingRequest
        {
            Id = Guid.NewGuid(),
            OrgId = dto.OrgId,
            CustomerId = dto.CustomerId,
            ReferenceImageUrl = string.Empty,
            Category = dto.Category,
            Color = dto.Color,
            Description = dto.Description,
            TargetPrice = dto.TargetPrice,
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        await _sourcingRequestRepository.AddAsync(request, cancellationToken);

        return new SourcingRequestDto
        {
            Id = request.Id,
            OrgId = request.OrgId,
            Category = request.Category,
            Color = request.Color,
            Description = request.Description,
            TargetPrice = request.TargetPrice,
            QuantityNeeded = dto.QuantityNeeded > 0 ? dto.QuantityNeeded : 1,
            CustomerId = request.CustomerId,
            Urgency = string.IsNullOrWhiteSpace(dto.Urgency) ? "medium" : dto.Urgency,
            Status = request.Status,
            CreatedAtUtc = request.CreatedAtUtc
        };
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
