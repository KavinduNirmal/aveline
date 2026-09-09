using Aveline.Domain.Entities;
using Aveline.Infrastructure.Persistence;
using Aveline.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Tests.Integration;

public class InventoryRepositoryTests
{
    private AvelineDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AvelineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AvelineDbContext(options);
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
}
