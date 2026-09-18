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
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orgId, string? cat, string? col, string? size, decimal? minP, decimal? maxP, bool inStock, int page, int pageSize, CancellationToken ct) =>
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
                    (!inStock || x.Quantity > 0)
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
}
