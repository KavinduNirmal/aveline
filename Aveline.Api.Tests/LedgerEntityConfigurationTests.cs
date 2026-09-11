using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #190 — EF model configuration for the entitlement ledger (domain-model.md §4).
/// </summary>
public class LedgerEntityConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"LedgerConfig_{Guid.NewGuid()}")
            .Options);

    private static IModel DesignTimeModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    [Theory]
    [InlineData(typeof(BlossomLedgerEntry), "BlossomLedgerEntries")]
    [InlineData(typeof(IdempotencyRecord), "IdempotencyRecords")]
    public void Entity_MapsToExpectedTable(Type entityType, string expectedTable)
    {
        using var context = CreateContext();
        Assert.Equal(expectedTable, context.Model.FindEntityType(entityType)!.GetTableName());
    }

    [Fact]
    public void LedgerEntry_DeclaresExpectedConstraints()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(BlossomLedgerEntry))!;
        var constraints = entity.GetCheckConstraints().Select(c => c.Name).ToArray();

        Assert.Contains("CK_BlossomLedgerEntries_Delta", constraints);
        Assert.Contains("CK_BlossomLedgerEntries_Reason", constraints);
        Assert.Contains("CK_BlossomLedgerEntries_Expiry", constraints);
    }

    [Fact]
    public void LedgerEntry_PersistsEnumsAsStrings()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(BlossomLedgerEntry))!;

        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(BlossomLedgerEntry.EntryType))!.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(BlossomLedgerEntry.SourceKind))!.GetTypeMapping().Converter?.ProviderClrType);
    }

    [Fact]
    public void LedgerEntry_HasFilteredUniqueIdempotencyIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(BlossomLedgerEntry))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Any(p => p.Name == nameof(BlossomLedgerEntry.IdempotencyKey)));

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[]
            {
                nameof(BlossomLedgerEntry.OrganizationId),
                nameof(BlossomLedgerEntry.IdempotencyScope),
                nameof(BlossomLedgerEntry.IdempotencyKey),
            },
            index.Properties.Select(p => p.Name));
        Assert.Contains("IdempotencyKey", index.GetFilter());
    }

    [Fact]
    public void UsageAccount_DeclaresBalanceCheckAndConcurrencyToken()
    {
        using var context = CreateContext();

        var constraints = DesignTimeModel(context)
            .FindEntityType(typeof(UsageAccount))!
            .GetCheckConstraints()
            .Select(c => c.Name)
            .ToArray();
        Assert.Contains("CK_UsageAccounts_Balance", constraints);

        var token = context.Model.FindEntityType(typeof(UsageAccount))!
            .FindProperty(nameof(UsageAccount.ConcurrencyToken));
        Assert.NotNull(token);
        Assert.True(token!.IsConcurrencyToken);
    }

    [Fact]
    public void IdempotencyRecord_HasReplayUniqueIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(IdempotencyRecord))!;

        var index = entity.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal(
            new[]
            {
                nameof(IdempotencyRecord.OrganizationId),
                nameof(IdempotencyRecord.Endpoint),
                nameof(IdempotencyRecord.IdempotencyKey),
            },
            index.Properties.Select(p => p.Name));
    }
}
