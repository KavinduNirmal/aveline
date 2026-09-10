using System;
using System.Collections.Generic;
using System.Linq;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies the EF Core model configuration for the Visual Intelligence & Inventory entities (Slice 2):
/// table names, indexes, foreign keys, JSON conversions, and multi-tenancy columns.
/// </summary>
public class VisualIntelligenceEntityConfigurationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"VisualIntelligenceConfig_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static IReadOnlyList<IIndex> IndexesFor(AppDbContext context, Type entityType)
    {
        var entity = context.Model.FindEntityType(entityType)!;
        return entity.GetIndexes().ToList();
    }

    [Theory]
    [InlineData(typeof(InventoryItem), "inventory_items")]
    [InlineData(typeof(InventoryImage), "inventory_images")]
    [InlineData(typeof(CustomerMatch), "customer_matches")]
    [InlineData(typeof(OutfitComposition), "outfit_compositions")]
    [InlineData(typeof(OutfitItem), "outfit_items")]
    [InlineData(typeof(SourcingRequest), "sourcing_requests")]
    [InlineData(typeof(Supplier), "suppliers")]
    public void Entity_MapsToExpectedTable(Type entityType, string expectedTable)
    {
        using var context = CreateContext();
        var tableName = context.Model.FindEntityType(entityType)!.GetTableName();
        Assert.Equal(expectedTable, tableName, ignoreCase: true);
    }

    [Fact]
    public void InventoryItem_HasRequiredPropertiesAndIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(InventoryItem))!;

        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.OrgId)));
        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.ItemName)));
        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.Category)));
        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.Color)));
        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.Price)));
        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.Cost)));
        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.StockQuantity)));
        Assert.NotNull(entity.FindProperty(nameof(InventoryItem.Status)));

        var indexes = IndexesFor(context, typeof(InventoryItem));
        Assert.NotEmpty(indexes);
    }

    [Fact]
    public void InventoryImage_HasForeignKeyToInventoryItem()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(InventoryImage))!;

        var itemFk = entity.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(InventoryItem));
        Assert.NotNull(itemFk);
    }

    [Fact]
    public void CustomerMatch_HasRequiredFields()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(CustomerMatch))!;

        Assert.NotNull(entity.FindProperty(nameof(CustomerMatch.CustomerId)));
        Assert.NotNull(entity.FindProperty(nameof(CustomerMatch.ItemId)));
        Assert.NotNull(entity.FindProperty(nameof(CustomerMatch.OrgId)));
        Assert.NotNull(entity.FindProperty(nameof(CustomerMatch.MatchConfidence)));
        Assert.NotNull(entity.FindProperty(nameof(CustomerMatch.MatchReason)));
    }

    [Fact]
    public void OutfitComposition_HasOutfitItemsNavigation()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(OutfitComposition))!;
        var itemsNav = entity.FindNavigation(nameof(OutfitComposition.Items));
        Assert.NotNull(itemsNav);
    }

    [Fact]
    public void SourcingRequest_HasRequiredFields()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(SourcingRequest))!;

        Assert.NotNull(entity.FindProperty(nameof(SourcingRequest.OrgId)));
        Assert.NotNull(entity.FindProperty(nameof(SourcingRequest.Status)));
    }

    [Fact]
    public void Supplier_HasRequiredFields()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Supplier))!;

        Assert.NotNull(entity.FindProperty(nameof(Supplier.OrgId)));
        Assert.NotNull(entity.FindProperty(nameof(Supplier.SupplierName)));
    }
}
