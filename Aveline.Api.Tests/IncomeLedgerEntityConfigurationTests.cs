using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Revenue.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R1 (issue #342) — EF model configuration for the append-only income ledger.
///
/// The entity is modelled on the Blossom ledger, which is the only append-only money journal in
/// this codebase, so the assertions here mirror <c>LedgerEntityConfigurationTests</c>: an explicit
/// precision, enum-as-string columns, a filtered unique dedup index, and a check constraint that
/// is declared in the model (so it is assertable) rather than only in the migration SQL.
/// </summary>
public class IncomeLedgerEntityConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"IncomeLedgerConfig_{Guid.NewGuid()}")
            .Options);

    private static IModel DesignTimeModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    [Fact]
    public void Entity_MapsToExpectedTable()
    {
        using var context = CreateContext();
        Assert.Equal(
            "IncomeLedgerEntries",
            context.Model.FindEntityType(typeof(IncomeLedgerEntry))!.GetTableName());
    }

    [Fact]
    public void Amount_IsMoneyPrecision_NotBlossomPrecision()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(IncomeLedgerEntry))!;

        // `decimal(18,2)` — money. The Blossom ledger uses `decimal(18,4)` because its unit is
        // fractional; copying that precision here would let a half-cent into a currency column.
        var amount = entity.FindProperty(nameof(IncomeLedgerEntry.Amount))!;
        Assert.Equal(18, amount.GetPrecision());
        Assert.Equal(2, amount.GetScale());
    }

    [Theory]
    [InlineData(nameof(IncomeLedgerEntry.Kind))]
    [InlineData(nameof(IncomeLedgerEntry.SourceKind))]
    [InlineData(nameof(IncomeLedgerEntry.ChargeBasis))]
    [InlineData(nameof(IncomeLedgerEntry.Status))]
    public void Enums_PersistAsStrings(string propertyName)
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(IncomeLedgerEntry))!;

        Assert.Equal(
            typeof(string),
            entity.FindProperty(propertyName)!.GetTypeMapping().Converter?.ProviderClrType);
    }

    [Fact]
    public void DeclaresTheAmountConstraint()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(IncomeLedgerEntry))!;
        var constraints = entity.GetCheckConstraints().Select(c => c.Name).ToArray();

        // `Amount` is stored positive and the sign is derived from `Kind`, so a non-positive
        // amount is a bug rather than a legitimate credit.
        Assert.Contains("CK_IncomeLedgerEntries_Amount", constraints);
    }

    [Fact]
    public void HasFilteredUniqueDedupIndexOnSourceKindAndSourceRef()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(IncomeLedgerEntry))!;

        var index = entity.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(IncomeLedgerEntry.SourceKind),
                nameof(IncomeLedgerEntry.SourceRef),
            }));

        Assert.True(index.IsUnique);
        Assert.Equal("\"SourceRef\" IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void ReasonAndSourceRef_AreBounded()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(IncomeLedgerEntry))!;

        Assert.Equal(500, entity.FindProperty(nameof(IncomeLedgerEntry.Reason))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(IncomeLedgerEntry.SourceRef))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(IncomeLedgerEntry.IdempotencyKey))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(IncomeLedgerEntry.IdempotencyScope))!.GetMaxLength());
    }

    [Fact]
    public void DeclaresTheThreeReadIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(IncomeLedgerEntry))!;

        var shapes = entity.GetIndexes()
            .Select(i => string.Join("+", i.Properties.Select(p => p.Name)))
            .ToArray();

        Assert.Contains(nameof(IncomeLedgerEntry.OccurredAt), shapes);
        Assert.Contains(
            $"{nameof(IncomeLedgerEntry.OrganizationId)}+{nameof(IncomeLedgerEntry.OccurredAt)}",
            shapes);
        Assert.Contains(
            $"{nameof(IncomeLedgerEntry.Kind)}+{nameof(IncomeLedgerEntry.OccurredAt)}",
            shapes);
    }

    [Fact]
    public void DeclaresTheExpectedIndexCount()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(IncomeLedgerEntry))!;

        // Four: the dedup identity plus the three read shapes. Asserted as a count so a new index
        // is a deliberate act rather than an accident.
        Assert.Equal(4, entity.GetIndexes().Count());
    }
}
