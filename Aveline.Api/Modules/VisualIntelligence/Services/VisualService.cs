using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Common.Media;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public class VisualService : IVisualService
{
    private readonly IInventoryService _inventoryService;
    private readonly ISourcingRequestRepository _sourcingRequestRepository;
    private readonly ICustomerMatchRepository _customerMatchRepository;
    private readonly IOutfitRepository _outfitRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IVisionService _visionService;
    private readonly IQrCodeService _qrCodeService;
    private readonly IMediaAssetLocator _mediaAssets;
    private readonly MediaTokenMintService _mediaTokens;
    private readonly ILogger<VisualService> _logger;

    public VisualService(
        IInventoryService inventoryService,
        ISourcingRequestRepository sourcingRequestRepository,
        ICustomerMatchRepository customerMatchRepository,
        IOutfitRepository outfitRepository,
        ISupplierRepository supplierRepository,
        IVisionService visionService,
        IQrCodeService qrCodeService,
        IMediaAssetLocator mediaAssets,
        MediaTokenMintService mediaTokens,
        ILogger<VisualService> logger)
    {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _sourcingRequestRepository = sourcingRequestRepository ?? throw new ArgumentNullException(nameof(sourcingRequestRepository));
        _customerMatchRepository = customerMatchRepository ?? throw new ArgumentNullException(nameof(customerMatchRepository));
        _outfitRepository = outfitRepository ?? throw new ArgumentNullException(nameof(outfitRepository));
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
        _visionService = visionService ?? throw new ArgumentNullException(nameof(visionService));
        _qrCodeService = qrCodeService ?? throw new ArgumentNullException(nameof(qrCodeService));
        _mediaAssets = mediaAssets ?? throw new ArgumentNullException(nameof(mediaAssets));
        _mediaTokens = mediaTokens ?? throw new ArgumentNullException(nameof(mediaTokens));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    public async Task<bool> DeleteInventoryItemAsync(
        Guid itemId,
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.DeleteItemAsync(itemId, orgId, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItemDto>> GetLowStockInventoryAsync(
        Guid orgId,
        int threshold = 5,
        CancellationToken cancellationToken = default)
    {
        return await _inventoryService.GetLowStockItemsAsync(orgId, threshold, cancellationToken);
    }

    /// <summary>
    /// Analyses an image, either from a named reference (resolved, tenant-checked, validated and
    /// minted here) or from a caller-supplied external URL (the permissive compatibility arm).
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// A named reference that is unknown, deleted or another organisation's. The caller maps it to
    /// a <c>404</c> — a reference the caller cannot see never yields a token or an image
    /// (strategy §3.5, migration plan §7.5).
    /// </exception>
    public async Task<ImageAnalysisResultDto> AnalyzeImageAsync(
        AnalyzeImageDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // The reference arm is additive. `ImageUrl` is non-nullable and empty means absent
        // (strategy §4 C14), so a stale "" never shadows a named reference; and `externalUrl` or an
        // unknown kind deliberately falls through to the ImageUrl arm, which is the arm Q7 left
        // permissive.
        if (TryReadReference(dto, out var imageRefKind, out var imageRefId))
        {
            return await AnalyzeReferenceAsync(dto, imageRefKind, imageRefId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _visionService
            .AnalyzeAsync(dto.ImageUrl, dto.OrgId, dto.FileName, dto.ContextHint, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the reference, checks the organisation, validates the analysable subset, mints and
    /// hands <see cref="IVisionService"/> an absolute tokenised URL (migration plan §8.2). The
    /// token is never returned to the caller: it exists only inside this call, so the caller's
    /// request and response carry no credential.
    /// </summary>
    private async Task<ImageAnalysisResultDto> AnalyzeReferenceAsync(
        AnalyzeImageDto dto,
        string imageRefKind,
        Guid imageRefId,
        CancellationToken cancellationToken)
    {
        // 1. Resolve. The lookup is tenant-scoped by construction (`IMediaAssetLocator` carries the
        //    organisation in its predicate), so a cross-org reference is null here — the same
        //    discipline CatalogEndpoints.cs:637 and ConversationService.cs:381 apply.
        var asset = await _mediaAssets
            .FindByReferenceAsync(dto.OrgId, imageRefKind, imageRefId, cancellationToken)
            .ConfigureAwait(false);

        if (asset is null)
        {
            throw new KeyNotFoundException("The referenced image was not found.");
        }

        // 2. The analysable subset, checked BEFORE the mint (strategy §3.6). The store admits nine
        //    image types; the provider reads four. An unreadable asset is recorded as such and left
        //    alone — never minted for a fetch that would fail, never silently degraded.
        if (!VisionContentTypes.IsAnalysable(asset.ContentType))
        {
            _logger.LogWarning(
                "Vision analysis skipped for reference {ImageRefKind}/{ImageRefId}: stored content "
                + "type {ContentType} is outside VisionContentTypes.IsAnalysable; recorded as not "
                + "analysable (strategy §3.6).",
                imageRefKind,
                imageRefId,
                asset.ContentType);

            return NotAnalysable();
        }

        // 3. Mint through the media module's mint service with the single-use `vision.analyze`
        //    scope, then pass the absolute URL to the provider.
        var minted = _mediaTokens.Mint(dto.OrgId, asset.AssetKey, MediaScope.VisionAnalyze);
        if (!minted.IsSuccess || string.IsNullOrWhiteSpace(minted.Url))
        {
            // `Mint` only refuses a reference it cannot see, which step 1 already excluded.
            throw new KeyNotFoundException("The referenced image was not found.");
        }

        return await _visionService
            .AnalyzeAsync(minted.Url, dto.OrgId, dto.FileName, dto.ContextHint, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the request names a tokenisable reference. Only the two row kinds
    /// <see cref="MediaReferenceKinds"/> names are references; every other kind (including
    /// <c>externalUrl</c>) belongs to the compatibility arm.
    /// </summary>
    private static bool TryReadReference(AnalyzeImageDto dto, out string imageRefKind, out Guid imageRefId)
    {
        imageRefKind = dto.ImageRefKind?.Trim() ?? string.Empty;
        imageRefId = dto.ImageRefId ?? Guid.Empty;

        return imageRefId != Guid.Empty
               && imageRefKind is MediaReferenceKinds.Attachment or MediaReferenceKinds.InventoryImage;
    }

    /// <summary>
    /// The recorded outcome for a stored type the provider cannot read. A fresh instance each time:
    /// the result DTO is a mutable wire model and must not be shared between responses.
    /// </summary>
    private static ImageAnalysisResultDto NotAnalysable() => new()
    {
        Category = "unknown",
        PrimaryColor = "unknown",
        ConfidenceScore = 0,
        IsFallback = true,
        NotAnalysable = true,
    };

    public async Task<IReadOnlyList<CustomerMatchDto>> GetCustomerMatchesAsync(
        Guid itemId,
        Guid orgId,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        return await _customerMatchRepository.GetEnrichedMatchesByItemIdAsync(itemId, orgId, minScore, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMatchDto>> GenerateCustomerMatchesAsync(
        Guid itemId,
        GenerateCustomerMatchesDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return await _customerMatchRepository.GenerateMatchesForInventoryItemAsync(itemId, dto.OrgId, dto.MaxMatches, cancellationToken);
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

    public async Task<IReadOnlyList<OutfitCompositionDto>> GetLookbooksByOrgIdAsync(
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        var outfits = await _outfitRepository.GetByOrgIdAsync(orgId, cancellationToken);
        var dtos = new List<OutfitCompositionDto>();

        foreach (var outfit in outfits)
        {
            var itemDetails = new List<OutfitItemDetailDto>();
            string? heroImageUrl = null;

            foreach (var item in outfit.Items)
            {
                var inv = await _inventoryService.GetItemByIdAsync(item.InventoryItemId, orgId, cancellationToken);
                if (inv != null)
                {
                    if (item.Role == "primary" && heroImageUrl == null)
                    {
                        heroImageUrl = inv.ImageUrl;
                    }
                    itemDetails.Add(new OutfitItemDetailDto
                    {
                        Id = item.Id,
                        InventoryItemId = item.InventoryItemId,
                        Role = item.Role,
                        ItemName = inv.ItemName,
                        Category = inv.Category,
                        Price = inv.Price,
                        ImageUrl = inv.ImageUrl
                    });
                }
            }

            dtos.Add(new OutfitCompositionDto
            {
                Id = outfit.Id,
                OrganizationId = outfit.OrgId,
                CustomerId = outfit.CustomerId,
                Name = outfit.Name,
                Occasion = outfit.Occasion,
                TotalPrice = outfit.TotalPrice,
                StyleNotes = outfit.StyleNotes,
                HeroImageUrl = heroImageUrl,
                CreatedAtUtc = outfit.CreatedAtUtc,
                Items = itemDetails
            });
        }

        return dtos;
    }

    public async Task<IReadOnlyList<SourcingRequestDto>> GetSourcingRequestsByOrgIdAsync(
        Guid orgId,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var requests = await _sourcingRequestRepository.GetByOrgIdAsync(orgId, status, cancellationToken);
        return requests.Select(r => new SourcingRequestDto
        {
            Id = r.Id,
            OrgId = r.OrgId,
            Category = r.Category,
            Color = r.Color,
            Description = r.Description,
            TargetPrice = r.TargetPrice,
            CustomerId = r.CustomerId,
            Status = r.Status,
            CreatedAtUtc = r.CreatedAtUtc
        }).ToList();
    }

    public async Task<SourcingRequestDto?> UpdateSourcingRequestStatusAsync(
        Guid id,
        Guid orgId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var request = await _sourcingRequestRepository.GetByIdAsync(id, orgId, cancellationToken);
        if (request == null)
        {
            return null;
        }

        request.Status = status;
        await _sourcingRequestRepository.UpdateAsync(request, cancellationToken);

        return new SourcingRequestDto
        {
            Id = request.Id,
            OrgId = request.OrgId,
            Category = request.Category,
            Color = request.Color,
            Description = request.Description,
            TargetPrice = request.TargetPrice,
            CustomerId = request.CustomerId,
            Status = request.Status,
            CreatedAtUtc = request.CreatedAtUtc
        };
    }

    public async Task<IReadOnlyList<SupplierDto>> GetSuppliersByOrgIdAsync(
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        var suppliers = await _supplierRepository.GetByOrgIdAsync(orgId, cancellationToken);
        return suppliers.Select(s => new SupplierDto
        {
            Id = s.Id,
            OrganizationId = s.OrgId,
            SupplierName = s.SupplierName,
            ContactEmail = s.ContactEmail,
            ContactPhone = s.ContactPhone,
            MinimumOrder = s.MinimumOrder,
            DeliveryTimeDays = s.DeliveryTimeDays,
            IsActive = s.IsActive,
            CreatedAtUtc = s.CreatedAtUtc
        }).ToList();
    }

    public async Task<IReadOnlyList<SupplierCatalogItemDto>> GetSupplierCatalogAsync(
        Guid supplierId,
        Guid orgId,
        string? category = null,
        string? color = null,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, orgId, cancellationToken);
        if (supplier == null)
        {
            return Array.Empty<SupplierCatalogItemDto>();
        }

        var catalog = new List<SupplierCatalogItemDto>
        {
            new()
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.SupplierName,
                Sku = $"{supplier.SupplierName.Substring(0, Math.Min(3, supplier.SupplierName.Length)).ToUpperInvariant()}-EM-01",
                ItemName = $"Authentic Pure Zari Silk Saree from {supplier.SupplierName}",
                Category = "saree",
                Color = "emerald",
                WholesalePrice = 38000m,
                LeadTimeDays = supplier.DeliveryTimeDays ?? 5,
                AvailableStock = 20,
                ImageUrl = "https://example.com/supplier/saree1.jpg"
            },
            new()
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.SupplierName,
                Sku = $"{supplier.SupplierName.Substring(0, Math.Min(3, supplier.SupplierName.Length)).ToUpperInvariant()}-RD-02",
                ItemName = $"Crimson Bridal Silk Saree from {supplier.SupplierName}",
                Category = "saree",
                Color = "crimson",
                WholesalePrice = 42000m,
                LeadTimeDays = supplier.DeliveryTimeDays ?? 7,
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
            var trimmedColor = color.Trim();
            query = query.Where(x =>
                x.Color.Contains(trimmedColor, StringComparison.OrdinalIgnoreCase) ||
                trimmedColor.Contains(x.Color, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(x.ItemName) && x.ItemName.Contains(trimmedColor, StringComparison.OrdinalIgnoreCase)));
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(x => x.WholesalePrice <= maxPrice.Value);
        }

        return query.ToList();
    }

    public async Task<QrCodeResponseDto> GenerateItemQrDtoAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default)
    {
        return await _qrCodeService.GenerateItemQrDtoAsync(orgId, itemId, format, size, cancellationToken);
    }

    public async Task<byte[]> GenerateItemQrBytesAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default)
    {
        return await _qrCodeService.GenerateItemQrBytesAsync(orgId, itemId, format, size, cancellationToken);
    }

    public QrCodeResponseDto GenerateQrResponse(GenerateQrDto dto)
    {
        return _qrCodeService.GenerateQrResponse(dto);
    }

    public async Task<QrScanResultDto> ScanAndResolveAsync(
        Guid orgId,
        ScanQrDto request,
        CancellationToken cancellationToken = default)
    {
        return await _qrCodeService.ScanAndResolveAsync(orgId, request, cancellationToken);
    }

    public async Task<QrScanResultDto> ScanAndResolveImageBytesAsync(
        Guid orgId,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        return await _qrCodeService.ScanAndResolveImageBytesAsync(orgId, imageBytes, cancellationToken);
    }
}
