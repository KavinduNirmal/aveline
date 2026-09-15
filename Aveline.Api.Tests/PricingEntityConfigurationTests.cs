using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #184 — EF model configuration for the pricing entities (domain-model.md §3).
/// Relational metadata (check constraints) lives in the design-time model, not the
/// read-optimized runtime model.
/// </summary>
public class PricingEntityConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PricingConfig_{Guid.NewGuid()}")
            .Options);

    private static IModel DesignTimeModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    [Theory]
    [InlineData(typeof(BlossomConversionRule), "BlossomConversionRules")]
    [InlineData(typeof(BlossomPriceEntry), "BlossomPriceEntries")]
    public void Entity_MapsToExpectedTable(Type entityType, string expectedTable)
    {
        using var context = CreateContext();
        Assert.Equal(expectedTable, context.Model.FindEntityType(entityType)!.GetTableName());
    }

    [Fact]
    public void ConversionRule_PersistsEnumsAsStrings()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(BlossomConversionRule))!;

        Assert.Equal(typeof(string), ProviderType(entity, nameof(BlossomConversionRule.ScopeKind)));
        Assert.Equal(typeof(string), ProviderType(entity, nameof(BlossomConversionRule.Status)));
        Assert.Equal(typeof(string), ProviderType(entity, nameof(BlossomConversionRule.RoundingMode)));
    }

    [Fact]
    public void ConversionRule_DeclaresExpectedConstraints()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(BlossomConversionRule))!;
        var constraints = entity.GetCheckConstraints().Select(c => c.Name).ToArray();

        Assert.Contains("CK_BlossomConversionRules_Scope", constraints);
        Assert.Contains("CK_BlossomConversionRules_Range", constraints);
        Assert.Contains("CK_BlossomConversionRules_Units", constraints);
        Assert.Contains("CK_BlossomConversionRules_Minimum", constraints);
        Assert.Contains("CK_BlossomConversionRules_Decimals", constraints);
    }

    [Fact]
    public void ConversionRule_HasUniqueScopeWindowIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(BlossomConversionRule))!;

        var unique = entity.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal(
            new[]
            {
                nameof(BlossomConversionRule.ScopeKind),
                nameof(BlossomConversionRule.Provider),
                nameof(BlossomConversionRule.Model),
                nameof(BlossomConversionRule.EffectiveFrom),
            },
            unique.Properties.Select(p => p.Name));
    }

    [Fact]
    public void PriceEntry_DeclaresExpectedConstraints()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(BlossomPriceEntry))!;
        var constraints = entity.GetCheckConstraints().Select(c => c.Name).ToArray();

        Assert.Contains("CK_BlossomPriceEntries_Quantity", constraints);
        Assert.Contains("CK_BlossomPriceEntries_Price", constraints);
        Assert.Contains("CK_BlossomPriceEntries_Range", constraints);
    }

    [Fact]
    public void PriceEntry_MapsSkuKindAsString_AndPlanTierNullable()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(BlossomPriceEntry))!;

        Assert.Equal(typeof(string), ProviderType(entity, nameof(BlossomPriceEntry.SkuKind)));
        Assert.Equal(typeof(string), ProviderType(entity, nameof(BlossomPriceEntry.Status)));
        Assert.True(entity.FindProperty(nameof(BlossomPriceEntry.PlanTier))!.IsNullable);
    }

    [Fact]
    public void ConversionRule_UsesXminConcurrencyToken()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(BlossomConversionRule))!;

        var token = entity.FindProperty(nameof(BlossomConversionRule.ConcurrencyToken));
        Assert.NotNull(token);
        Assert.True(token!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, token.ValueGenerated);
    }

    private static Type? ProviderType(IEntityType entity, string propertyName)
    {
        var property = entity.FindProperty(propertyName)!;
        return property.GetTypeMapping().Converter?.ProviderClrType
               ?? property.GetValueConverter()?.ProviderClrType;
    }
}
