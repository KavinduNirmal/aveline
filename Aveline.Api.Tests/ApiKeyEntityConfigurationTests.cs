using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #201 — EF model configuration for API keys and the organization setting
/// columns (domain-model.md §5).
/// </summary>
public class ApiKeyEntityConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ApiKeyConfig_{Guid.NewGuid()}")
            .Options);

    private static IModel DesignTimeModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    [Fact]
    public void ApiKey_MapsToExpectedTable()
    {
        using var context = CreateContext();
        Assert.Equal("ApiKeys", context.Model.FindEntityType(typeof(ApiKey))!.GetTableName());
    }

    [Fact]
    public void ApiKey_HasUniquePrefixIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiKey))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(ApiKey.Prefix));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void ApiKey_HasOrgStatusIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiKey))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(ApiKey.OrganizationId), nameof(ApiKey.Status) }));

        Assert.False(index.IsUnique);
    }

    [Fact]
    public void ApiKey_HasFilteredExpiryIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiKey))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(ApiKey.ExpiresAt));

        Assert.Contains("ExpiresAt", index.GetFilter());
        Assert.Contains("Active", index.GetFilter());
    }

    [Fact]
    public void ApiKey_PersistsEnumsAsStrings()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(ApiKey))!;

        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(ApiKey.Environment))!.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(ApiKey.Status))!.GetTypeMapping().Converter?.ProviderClrType);
    }

    [Theory]
    [InlineData(nameof(ApiKey.Name), 100)]
    [InlineData(nameof(ApiKey.Prefix), 16)]
    [InlineData(nameof(ApiKey.KeyHash), 128)]
    [InlineData(nameof(ApiKey.HashAlgorithm), 32)]
    [InlineData(nameof(ApiKey.RevokedReason), 300)]
    [InlineData(nameof(ApiKey.LastUsedIpHash), 64)]
    public void ApiKey_HasExpectedMaxLengths(string propertyName, int expectedLength)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(ApiKey))!
            .FindProperty(propertyName)!;

        Assert.Equal(expectedLength, property.GetMaxLength());
    }

    [Fact]
    public void ApiKey_ScopesAreRequired()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(ApiKey))!
            .FindProperty(nameof(ApiKey.Scopes))!;

        Assert.False(property.IsNullable);
    }

    [Fact]
    public void Organization_HasApiKeySettingColumns()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Organization))!;

        Assert.NotNull(entity.FindProperty(nameof(Organization.BillingEmail)));
        Assert.NotNull(entity.FindProperty(nameof(Organization.ContactEmail)));
        Assert.NotNull(entity.FindProperty(nameof(Organization.Currency)));
        Assert.NotNull(entity.FindProperty(nameof(Organization.TimeZone)));
        Assert.NotNull(entity.FindProperty(nameof(Organization.SuspendedAt)));

        Assert.Equal(254, entity.FindProperty(nameof(Organization.BillingEmail))!.GetMaxLength());
        Assert.Equal(254, entity.FindProperty(nameof(Organization.ContactEmail))!.GetMaxLength());
        Assert.Equal(3, entity.FindProperty(nameof(Organization.Currency))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(Organization.TimeZone))!.GetMaxLength());
    }

    [Fact]
    public void OrganizationMembership_HasUniqueUserOrganizationIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(OrganizationMembership))!;

        var index = entity.GetIndexes().Single(i => i.IsUnique);

        Assert.Equal(
            new[] { nameof(OrganizationMembership.OrganizationId), nameof(OrganizationMembership.UserId) },
            index.Properties.Select(p => p.Name));
    }
}
