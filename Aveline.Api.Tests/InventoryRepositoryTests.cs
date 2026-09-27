using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Api.Tests;

public class InventoryRepositoryTests
{
    private AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task SearchAsync_WithEFCoreLINQ_FiltersByOrgIdStatusAndSoftDelete()
    {
        // Arrange
        using var db = CreateInMemoryContext();
        var repository = new InventoryRepository(db);
        var orgId = Guid.NewGuid();

        var item1 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Emerald Silk Saree",
            Category = "saree",
            Color = "emerald",
            Price = 45000,
            Quantity = 3,
            Sizes = new List<string> { "FreeSize" },
            Status = "available",
            DeletedAt = null
        };

        var item2 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Emerald Cotton Saree",
            Category = "saree",
            Color = "emerald",
            Price = 15000,
            Quantity = 5,
            Sizes = new List<string> { "FreeSize" },
            Status = "available",
            DeletedAt = DateTime.UtcNow
        };

        var item3 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Emerald Chiffon Saree",
            Category = "saree",
            Color = "emerald",
            Price = 25000,
            Quantity = 2,
            Sizes = new List<string> { "FreeSize" },
            Status = "archived",
            DeletedAt = null
        };

        var item4 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = Guid.NewGuid(),
            ItemName = "Emerald Silk Saree",
            Category = "saree",
            Color = "emerald",
            Price = 45000,
            Quantity = 3,
            Sizes = new List<string> { "FreeSize" },
            Status = "available",
            DeletedAt = null
        };

        await db.InventoryItems.AddRangeAsync(item1, item2, item3, item4);
        await db.SaveChangesAsync();

        // Act
        var results = await repository.SearchAsync(
            orgId: orgId,
            category: "saree",
            color: "emerald",
            inStockOnly: true
        );

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(item1.Id);
        results[0].ItemName.Should().Be("Emerald Silk Saree");
    }

    [Fact]
    public async Task SearchAsync_WithSizeFilterAndPagination_ReturnsExpectedPagedResults()
    {
        // Arrange
        using var db = CreateInMemoryContext();
        var repository = new InventoryRepository(db);
        var orgId = Guid.NewGuid();

        var item1 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Dress A",
            Category = "dress",
            Color = "black",
            Price = 10000,
            Quantity = 2,
            Sizes = new List<string> { "S", "M" },
            Status = "available"
        };

        var item2 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Dress B",
            Category = "dress",
            Color = "black",
            Price = 12000,
            Quantity = 4,
            Sizes = new List<string> { "M", "L" },
            Status = "available"
        };

        var item3 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Dress C",
            Category = "dress",
            Color = "black",
            Price = 14000,
            Quantity = 1,
            Sizes = new List<string> { "L", "XL" },
            Status = "available"
        };

        await db.InventoryItems.AddRangeAsync(item1, item2, item3);
        await db.SaveChangesAsync();

        // Act - filter for size "M", page 1, pageSize 1
        var page1 = await repository.SearchAsync(
            orgId: orgId,
            size: "M",
            page: 1,
            pageSize: 1
        );

        // Assert
        page1.Should().HaveCount(1);
        page1[0].ItemName.Should().Be("Dress A");

        // Act - page 2, pageSize 1
        var page2 = await repository.SearchAsync(
            orgId: orgId,
            size: "M",
            page: 2,
            pageSize: 1
        );

        page2.Should().HaveCount(1);
        page2[0].ItemName.Should().Be("Dress B");
    }

    [Fact]
    public async Task SearchAsync_WithFuzzyColorAndSubstring_MatchesVariedColorNames()
    {
        // Arrange
        using var db = CreateInMemoryContext();
        var repository = new InventoryRepository(db);
        var orgId = Guid.NewGuid();

        var item1 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Emerald Green Kanchipuram Saree",
            Category = "saree",
            Color = "Emerald Green",
            Price = 65000,
            Quantity = 2,
            Status = "available"
        };

        var item2 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Sage Green Linen Dress",
            Category = "dress",
            Color = "Sage Green",
            Price = 28000,
            Quantity = 4,
            Status = "available"
        };

        var item3 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Mint Silk Saree",
            Category = "saree",
            Color = "Mint Green",
            Price = 32000,
            Quantity = 1,
            Status = "available"
        };

        var item4 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Royal Crimson Velvet Gown",
            Category = "gown",
            Color = "Deep Crimson",
            Price = 85000,
            Quantity = 3,
            Status = "available"
        };

        await db.InventoryItems.AddRangeAsync(item1, item2, item3, item4);
        await db.SaveChangesAsync();

        // Act - search generic "green" matches all green shades
        var greenResults = await repository.SearchAsync(orgId: orgId, color: "green");
        var emeraldResults = await repository.SearchAsync(orgId: orgId, color: "emerald");
        var crimsonResults = await repository.SearchAsync(orgId: orgId, color: "crimson");

        // Assert
        greenResults.Should().HaveCount(3);
        greenResults.Select(x => x.ItemName).Should().Contain(new[] { "Emerald Green Kanchipuram Saree", "Sage Green Linen Dress", "Mint Silk Saree" });

        emeraldResults.Should().HaveCount(1);
        emeraldResults[0].ItemName.Should().Be("Emerald Green Kanchipuram Saree");

        crimsonResults.Should().HaveCount(1);
        crimsonResults[0].ItemName.Should().Be("Royal Crimson Velvet Gown");
    }

    [Fact]
    public async Task SearchAsync_WhenSearchingBlueSaree_DoesNotReturnBlueGowns()
    {
        // Arrange
        using var db = CreateInMemoryContext();
        var repository = new InventoryRepository(db);
        var orgId = Guid.NewGuid();

        var gown = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Powder Blue Duchess Satin Luminous Evening Gown",
            Category = "Gowns",
            Color = "Powder Blue",
            Price = 1250,
            Quantity = 4,
            Status = "available"
        };

        var saree = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Royal Blue Kanjivaram Silk Saree",
            Category = "Sarees",
            Color = "Royal Blue",
            Price = 35000,
            Quantity = 2,
            Status = "available"
        };

        await db.InventoryItems.AddRangeAsync(gown, saree);
        await db.SaveChangesAsync();

        // Act - Search specifically for category "Sarees" and color "Blue"
        var results = await repository.SearchAsync(
            orgId: orgId,
            category: "Sarees",
            color: "Blue"
        );

        // Assert - Only the blue saree is returned, NOT the blue gown
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(saree.Id);
        results[0].ItemName.Should().Be("Royal Blue Kanjivaram Silk Saree");
    }

    [Fact]
    public async Task SearchAsync_WhenSearchingWithConversationalQuery_TokensAndMatchesItem()
    {
        // Arrange
        using var db = CreateInMemoryContext();
        var repository = new InventoryRepository(db);
        var orgId = Guid.NewGuid();

        var saree = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Crimson Red Pure Mulberry Silk Banarasi Silk Brocade Saree",
            Category = "Sarees",
            Color = "Crimson Red",
            Price = 45000,
            Quantity = 4,
            Status = "available"
        };

        var gown = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Sky Blue Duchess Satin Luminous Evening Gown",
            Category = "Gowns",
            Color = "Sky Blue",
            Price = 1250,
            Quantity = 4,
            Status = "available"
        };

        await db.InventoryItems.AddRangeAsync(saree, gown);
        await db.SaveChangesAsync();

        // Act - Search with a conversational query string
        var results = await repository.SearchAsync(
            orgId: orgId,
            query: "do we have any red saree in stock"
        );

        // Assert - Correctly extracts significant terms and matches the crimson red saree
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(saree.Id);
        results[0].ItemName.Should().Be("Crimson Red Pure Mulberry Silk Banarasi Silk Brocade Saree");
    }

    [Fact]
    public async Task SearchAsync_WhenSearchingRedCategorySaree_MatchesCrimsonAndDeepCrimsonSarees()
    {
        // Arrange
        using var db = CreateInMemoryContext();
        var repository = new InventoryRepository(db);
        var orgId = Guid.NewGuid();

        var saree1 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Crimson Red Pure Mulberry Silk Banarasi Silk Brocade Saree",
            Category = "Sarees",
            Color = "Crimson Red",
            Price = 45000,
            Quantity = 4,
            Status = "available"
        };

        var saree2 = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Deep Crimson Pure Mulberry Silk Banarasi Silk Brocade Saree",
            Category = "Sarees",
            Color = "Deep Crimson",
            Description = "Exquisite deep crimson banarasi silk brocade saree featuring structured footwear pairing.",
            Price = 42000,
            Quantity = 3,
            Status = "available"
        };

        var peacockTealSaree = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Peacock Teal Pure Mulberry Silk Banarasi Silk Brocade Saree",
            Category = "Sarees",
            Color = "Peacock Teal",
            Description = "Exquisite peacock teal banarasi silk brocade saree with structured footwear styling.",
            Price = 42000,
            Quantity = 4,
            Status = "available"
        };

        var terracottaSaree = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Terracotta Pure Mulberry Silk Banarasi Silk Brocade Saree",
            Category = "Sarees",
            Color = "Terracotta",
            Description = "Exquisite terracotta banarasi silk brocade saree with structured silhouette.",
            Price = 42000,
            Quantity = 4,
            Status = "available"
        };

        var blueGown = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Powder Blue Duchess Satin Luminous Evening Gown",
            Category = "Gowns",
            Color = "Powder Blue",
            Price = 1250,
            Quantity = 4,
            Status = "available"
        };

        await db.InventoryItems.AddRangeAsync(saree1, saree2, peacockTealSaree, terracottaSaree, blueGown);
        await db.SaveChangesAsync();

        // Act - Search with category "Sarees" and color "Red"
        var results = await repository.SearchAsync(
            orgId: orgId,
            category: "Sarees",
            color: "Red"
        );

        // Assert - Matches only Red/Crimson sarees, never Peacock Teal, Terracotta, or Blue Gown
        results.Should().HaveCount(2);
        results.Select(x => x.Id).Should().Contain(new[] { saree1.Id, saree2.Id });
        results.Select(x => x.Id).Should().NotContain(new[] { peacockTealSaree.Id, terracottaSaree.Id, blueGown.Id });
    }
}
