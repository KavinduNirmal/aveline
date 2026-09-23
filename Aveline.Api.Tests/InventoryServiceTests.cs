using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

public class InventoryServiceTests
{
    private readonly Mock<IInventoryRepository> _repositoryMock;
    private readonly InventoryService _service;
    private readonly List<InventoryItem> _inMemoryItems = new();

    public InventoryServiceTests()
    {
        _repositoryMock = new Mock<IInventoryRepository>();

        _repositoryMock
            .Setup(r => r.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<decimal?>(),
                It.IsAny<decimal?>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orgId, string? cat, string? col, string? size, decimal? minP, decimal? maxP, bool inStock, int page, int pageSize, string? q, CancellationToken ct) =>
            {
                var query = _inMemoryItems.Where(x =>
                    x.OrgId == orgId &&
                    x.DeletedAt == null &&
                    x.Status == "available" &&
                    (col == null || x.Color.Equals(col, StringComparison.OrdinalIgnoreCase)) &&
                    (cat == null || x.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)) &&
                    (size == null || x.Sizes.Any(s => s.Equals(size, StringComparison.OrdinalIgnoreCase))) &&
                    (!minP.HasValue || x.Price >= minP.Value) &&
                    (!maxP.HasValue || x.Price <= maxP.Value) &&
                    (!inStock || x.Quantity > 0) &&
                    (q == null || (x.ItemName != null && x.ItemName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                  (x.Category != null && x.Category.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                  (x.Description != null && x.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
                );

                return query
                    .OrderBy(x => x.ItemName)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            });

        _service = new InventoryService(_repositoryMock.Object);
    }

    private Task SeedInventory(
        Guid orgId,
        string itemName,
        string category,
        string color,
        decimal price,
        int quantity,
        List<string>? sizes = null,
        string status = "available",
        DateTime? deletedAt = null)
    {
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = itemName,
            Category = category,
            Color = color,
            Price = price,
            Quantity = quantity,
            Sizes = sizes ?? new List<string> { "S", "M", "L" },
            Status = status,
            DeletedAt = deletedAt,
            CreatedAtUtc = DateTime.UtcNow
        };
        _inMemoryItems.Add(item);
        return Task.CompletedTask;
    }

    // 1. Organization isolation
    [Fact]
    public async Task SearchInventory_ShouldNeverReturnAnotherOrganizationsItems()
    {
        // Arrange
        var targetOrgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();

        await SeedInventory(targetOrgId, "Emerald Silk Saree", "saree", "emerald", 45000, 3);
        await SeedInventory(otherOrgId, "Ruby Saree", "saree", "ruby", 35000, 2);

        var request = new SearchInventoryDto
        {
            OrgId = targetOrgId
        };

        // Act
        var result = await _service.SearchInventoryAsync(request);

        // Assert
        result.Should().HaveCount(1);
        result[0].ItemName.Should().Be("Emerald Silk Saree");
        result.Should().NotContain(x => x.OrgId == otherOrgId);
    }

    // 2. Color filtering
    [Fact]
    public async Task SearchInventory_WithColor_ReturnsMatchingColor()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        await SeedInventory(orgId, "Emerald Silk Saree", "saree", "emerald", 45000, 3);
        await SeedInventory(orgId, "Midnight Blue Lehenga", "lehenga", "blue", 55000, 2);

        var request = new SearchInventoryDto
        {
            OrgId = orgId,
            Color = "emerald"
        };

        // Act
        var result = await _service.SearchInventoryAsync(request);

        // Assert
        result.Should().HaveCount(1);
        result[0].Color.Should().Be("emerald");
        result[0].ItemName.Should().Be("Emerald Silk Saree");
    }

    // 3. Category filtering
    [Fact]
    public async Task SearchInventory_WithCategory_ReturnsMatchingCategory()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        await SeedInventory(orgId, "Emerald Silk Saree", "saree", "emerald", 45000, 3);
        await SeedInventory(orgId, "Emerald Velvet Kurti", "kurti", "emerald", 12000, 5);

        var request = new SearchInventoryDto
        {
            OrgId = orgId,
            Category = "saree"
        };

        // Act
        var result = await _service.SearchInventoryAsync(request);

        // Assert
        result.Should().HaveCount(1);
        result[0].Category.Should().Be("saree");
        result[0].ItemName.Should().Be("Emerald Silk Saree");
    }

    // 4. Size filtering
    [Fact]
    public async Task SearchInventory_WithSize_ReturnsItemsContainingSize()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        await SeedInventory(orgId, "Summer Linen Dress", "dress", "white", 8500, 4, sizes: new List<string> { "M", "L", "XL" });
        await SeedInventory(orgId, "Petite Cocktail Dress", "dress", "black", 15000, 2, sizes: new List<string> { "XS", "S" });

        var request = new SearchInventoryDto
        {
            OrgId = orgId,
            Size = "M"
        };

        // Act
        var result = await _service.SearchInventoryAsync(request);

        // Assert
        result.Should().HaveCount(1);
        result[0].ItemName.Should().Be("Summer Linen Dress");
        result[0].Sizes.Should().Contain("M");
    }

    // 5. Budget filtering
    [Fact]
    public async Task SearchInventory_WithMaximumPrice_ReturnsAffordableItems()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        await SeedInventory(orgId, "Affordable Cotton Kurti", "kurti", "yellow", 3500, 10);
        await SeedInventory(orgId, "Designer Luxury Saree", "saree", "gold", 85000, 2);

        var request = new SearchInventoryDto
        {
            OrgId = orgId,
            MaximumPrice = 10000
        };

        // Act
        var result = await _service.SearchInventoryAsync(request);

        // Assert
        result.Should().HaveCount(1);
        result[0].ItemName.Should().Be("Affordable Cotton Kurti");
        result[0].Price.Should().BeLessThanOrEqualTo(10000);
    }

    // 6. Stock filtering
    [Fact]
    public async Task SearchInventory_ShouldNotReturnSoldOutItems()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        await SeedInventory(orgId, "In Stock Item", "saree", "red", 20000, 3);
        await SeedInventory(orgId, "Sold Out Item", "saree", "red", 20000, 0); // 0 quantity

        var request = new SearchInventoryDto
        {
            OrgId = orgId,
            InStockOnly = true
        };

        // Act
        var result = await _service.SearchInventoryAsync(request);

        // Assert
        result.Should().HaveCount(1);
        result[0].ItemName.Should().Be("In Stock Item");
        result.Should().NotContain(x => x.Quantity == 0);
    }

    // 7. Deleted items
    [Fact]
    public async Task SearchInventory_ShouldNotReturnSoftDeletedItems()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        await SeedInventory(orgId, "Active Item", "saree", "green", 18000, 2, deletedAt: null);
        await SeedInventory(orgId, "Deleted Item", "saree", "green", 18000, 2, deletedAt: DateTime.UtcNow);

        var request = new SearchInventoryDto
        {
            OrgId = orgId
        };

        // Act
        var result = await _service.SearchInventoryAsync(request);

        // Assert
        result.Should().HaveCount(1);
        result[0].ItemName.Should().Be("Active Item");
    }

    // 8. Pagination
    [Fact]
    public async Task SearchInventory_ShouldRespectPageAndPageSize()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        for (int i = 1; i <= 5; i++)
        {
            await SeedInventory(orgId, $"Item {i:D2}", "saree", "blue", 10000 * i, 5);
        }

        // Page 1 with pageSize 2 -> Items 01, 02
        var page1Request = new SearchInventoryDto
        {
            OrgId = orgId,
            Page = 1,
            PageSize = 2
        };

        // Page 2 with pageSize 2 -> Items 03, 04
        var page2Request = new SearchInventoryDto
        {
            OrgId = orgId,
            Page = 2,
            PageSize = 2
        };

        // Act
        var page1Result = await _service.SearchInventoryAsync(page1Request);
        var page2Result = await _service.SearchInventoryAsync(page2Request);

        // Assert
        page1Result.Should().HaveCount(2);
        page1Result.Select(x => x.ItemName).Should().ContainInOrder("Item 01", "Item 02");

        page2Result.Should().HaveCount(2);
        page2Result.Select(x => x.ItemName).Should().ContainInOrder("Item 03", "Item 04");
    }

    // 9. The analysed colour survives create → `InventoryItemDto.FromDomain`. Before `ColorHex`
    // existed the hex the vision model measured was silently dropped on the way to the database.
    [Fact]
    public async Task CreateItem_WithMeasuredColorHex_SurvivesCreateAndDtoMapping()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        InventoryItem? persisted = null;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Callback<InventoryItem, CancellationToken>((item, _) => persisted = item)
            .Returns(Task.CompletedTask);

        var dto = new CreateInventoryItemDto
        {
            OrgId = orgId,
            ItemName = "Fuchsia Bodycon Dress",
            Category = "Gowns",
            Color = "Fuchsia Pink",
            ColorHex = "#D5006D",
            Price = 18000,
            Cost = 8000,
            Quantity = 2
        };

        // Act
        var result = await _service.CreateItemAsync(dto);

        // Assert
        persisted.Should().NotBeNull();
        persisted!.ColorHex.Should().Be("#D5006D");
        result.ColorHex.Should().Be("#D5006D");
    }

    // 10. A supplied update replaces the stored measured colour.
    [Fact]
    public async Task UpdateItem_WithColorHex_SetsIt()
    {
        // Arrange
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var existing = new InventoryItem
        {
            Id = id,
            OrgId = orgId,
            ItemName = "Dress",
            Color = "Pink",
            ColorHex = "#111111",
            CreatedAtUtc = DateTime.UtcNow
        };
        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateItemAsync(
            id,
            new UpdateInventoryItemDto { OrgId = orgId, ColorHex = "#FF00FF" });

        // Assert
        existing.ColorHex.Should().Be("#FF00FF");
        result.Should().NotBeNull();
        result!.ColorHex.Should().Be("#FF00FF");
    }

    // 11. An update that says nothing about the colour must not erase what is stored. The field is
    // nullable, so "absent" and "explicitly null" are indistinguishable on the wire; the service
    // treats both as "leave it alone", exactly like `Description`.
    [Fact]
    public async Task UpdateItem_WithoutColorHex_LeavesTheStoredValueAlone()
    {
        // Arrange
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var existing = new InventoryItem
        {
            Id = id,
            OrgId = orgId,
            ItemName = "Dress",
            Color = "Pink",
            ColorHex = "#D5006D",
            CreatedAtUtc = DateTime.UtcNow
        };
        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act — a rename that carries no colour.
        var result = await _service.UpdateItemAsync(
            id,
            new UpdateInventoryItemDto { OrgId = orgId, ItemName = "Renamed Dress" });

        // Assert
        existing.ItemName.Should().Be("Renamed Dress");
        existing.ColorHex.Should().Be("#D5006D");
        result.Should().NotBeNull();
        result!.ColorHex.Should().Be("#D5006D");
    }

    // 12. Anything that is not a CSS hex literal is stored as "not measured", never verbatim. A
    // stored malformed value would be painted by the card as a real colour nobody measured.
    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("javascript:alert(1)")]
    [InlineData("#gggggg")]
    public async Task UpdateItem_WithMalformedColorHex_StoresNull(string malformed)
    {
        // Arrange
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var existing = new InventoryItem
        {
            Id = id,
            OrgId = orgId,
            ItemName = "Dress",
            Color = "Pink",
            ColorHex = "#D5006D",
            CreatedAtUtc = DateTime.UtcNow
        };
        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateItemAsync(
            id,
            new UpdateInventoryItemDto { OrgId = orgId, ColorHex = malformed });

        // Assert
        existing.ColorHex.Should().BeNull();
        result.Should().NotBeNull();
        result!.ColorHex.Should().BeNull();
    }

    // 13. The normaliser itself: trim, accept only `#` + exactly 3 or 6 hex digits, otherwise null.
    [Theory]
    [InlineData("#fff", "#fff")]
    [InlineData("#FFF", "#FFF")]
    [InlineData("#123456", "#123456")]
    [InlineData("#D5006D", "#D5006D")]
    [InlineData("  #abc  ", "#abc")]
    [InlineData("#12345", null)]
    [InlineData("#1234567", null)]
    [InlineData("red", null)]
    [InlineData("123456", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("#gggggg", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeColorHex_AcceptsOnlyCssHexLiterals(string? input, string? expected)
    {
        InventoryService.NormalizeColorHex(input).Should().Be(expected);
    }

    [Fact]
    public async Task SearchInventoryAsync_WithFreeTextQuery_ReturnsMatchingItems()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        await SeedInventory(orgId, "Peach Silk Evening Dress", "Evening", "Peach", 45000, 2);
        await SeedInventory(orgId, "Emerald Handloom Saree", "Traditional", "Emerald", 32000, 3);
        await SeedInventory(orgId, "Ivory Raw Silk Kurta", "Casual", "Ivory", 18000, 1);

        var searchDto = new SearchInventoryDto
        {
            OrgId = orgId,
            Query = "dress",
            InStockOnly = true,
        };

        // Act
        var results = await _service.SearchInventoryAsync(searchDto);

        // Assert
        results.Should().HaveCount(1);
        results[0].ItemName.Should().Be("Peach Silk Evening Dress");
    }

    [Fact]
    public async Task SearchInventoryAsync_WithQueryMatchingCategory_ReturnsMatchingItems()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        await SeedInventory(orgId, "Midnight Shimmer Piece", "Dress", "Black", 50000, 4);
        await SeedInventory(orgId, "Gold Filigree Belt", "Accessory", "Gold", 12000, 2);

        var searchDto = new SearchInventoryDto
        {
            OrgId = orgId,
            Query = "dress",
            InStockOnly = true,
        };

        // Act
        var results = await _service.SearchInventoryAsync(searchDto);

        // Assert
        results.Should().HaveCount(1);
        results[0].ItemName.Should().Be("Midnight Shimmer Piece");
    }
}

