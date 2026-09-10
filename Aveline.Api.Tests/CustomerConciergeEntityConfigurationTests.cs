using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies the EF Core model configuration for the Customer Concierge entities:
/// table names, indexes, column types (incl. the pgvector embedding), and soft-delete filters.
/// </summary>
public class CustomerConciergeEntityConfigurationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomerConciergeConfig_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static IReadOnlyList<IIndex> IndexesFor(AppDbContext context, Type entityType)
    {
        var entity = context.Model.FindEntityType(entityType)!;
        return entity.GetIndexes().ToList();
    }

    [Theory]
    [InlineData(typeof(Customer), "Customers")]
    [InlineData(typeof(CustomerPreference), "Customer_Preferences")]
    [InlineData(typeof(CustomerEvent), "Customer_Events")]
    [InlineData(typeof(CustomerMemory), "Customer_Memory")]
    [InlineData(typeof(CustomerInteraction), "Customer_Interactions")]
    [InlineData(typeof(CustomerConsent), "Customer_Consent")]
    [InlineData(typeof(CustomerTag), "Customer_Tags")]
    public void Entity_MapsToExpectedTable(Type entityType, string expectedTable)
    {
        using var context = CreateContext();
        var tableName = context.Model.FindEntityType(entityType)!.GetTableName();
        Assert.Equal(expectedTable, tableName);
    }

    [Fact]
    public void Customer_HasUniquePhonePerOrganization()
    {
        using var context = CreateContext();
        var indexes = IndexesFor(context, typeof(Customer));
        var unique = indexes.Single(i => i.IsUnique);
        Assert.Equal(new[] { "OrganizationId", "PhoneNumber" }, unique.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Customer_HasSoftDeleteQueryFilter()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Customer))!;
        Assert.NotNull(entity.GetQueryFilter());
    }

    [Fact]
    public void CustomerMemory_HasSoftDeleteQueryFilter()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(CustomerMemory))!;
        Assert.NotNull(entity.GetQueryFilter());
    }

    [Fact]
    public void CustomerMemory_MetadataJson_IsJsonb()
    {
        using var context = CreateContext();
        var columnType = context.Model.FindEntityType(typeof(CustomerMemory))!
            .FindProperty(nameof(CustomerMemory.MetadataJson))!
            .FindAnnotation("Relational:ColumnType")!.Value!.ToString();
        Assert.Equal("jsonb", columnType);
    }

    [Fact]
    public void CustomerInteraction_ParsedIntentJson_IsJsonb()
    {
        using var context = CreateContext();
        var columnType = context.Model.FindEntityType(typeof(CustomerInteraction))!
            .FindProperty(nameof(CustomerInteraction.ParsedIntentJson))!
            .FindAnnotation("Relational:ColumnType")!.Value!.ToString();
        Assert.Equal("jsonb", columnType);
    }

    [Fact]
    public void CustomerConsent_HasUniqueRowPerCustomer()
    {
        using var context = CreateContext();
        var indexes = IndexesFor(context, typeof(CustomerConsent));
        var unique = indexes.Single(i => i.IsUnique);
        Assert.Equal(new[] { "OrganizationId", "CustomerId" }, unique.Properties.Select(p => p.Name));
    }

    [Fact]
    public void CustomerTag_HasUniqueTagPerCustomer()
    {
        using var context = CreateContext();
        var indexes = IndexesFor(context, typeof(CustomerTag));
        var unique = indexes.Single(i => i.IsUnique);
        Assert.Equal(new[] { "CustomerId", "Tag" }, unique.Properties.Select(p => p.Name));
    }

    [Fact]
    public void CustomerMemory_HasCategoryIndex()
    {
        using var context = CreateContext();
        var indexes = IndexesFor(context, typeof(CustomerMemory));
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Category" }));
    }

    [Fact]
    public void CustomerInteraction_HasChannelIndex()
    {
        using var context = CreateContext();
        var indexes = IndexesFor(context, typeof(CustomerInteraction));
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Channel" }));
    }

    [Fact]
    public void CustomerMemory_Embedding_MigrationAddsVectorColumnAndHnswIndex()
    {
        // The pgvector embedding column + HNSW index are added via raw SQL in the migration
        // (not part of the EF model). Locate the migration that creates the Customer_Memory table.
        var migrationsDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "Aveline.Api", "Migrations");
        var migrationFile = Directory.GetFiles(migrationsDir, "*.cs")
            .FirstOrDefault(f => f.Contains("AddCustomerConcierge", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("AddCustomerConcierge migration not found.");

        var sql = File.ReadAllText(migrationFile);
        Assert.Contains("vector(1536)", sql);
        Assert.Contains("hnsw", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vector_cosine_ops", sql, StringComparison.OrdinalIgnoreCase);
    }
}
