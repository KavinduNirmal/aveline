using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Handbook.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies the EF Core model configuration for <see cref="HandbookChunk"/>: the table name, the
/// upsert key, and the column defaults the service relies on.
///
/// <para>
/// The two search columns (<c>embedding</c>, <c>SearchVector</c>) are deliberately absent from the
/// EF model (ADR-017, ADR-025) - the in-memory provider cannot map <c>vector</c> or
/// <c>tsvector</c> - so they are asserted in the Postgres test instead, where a real database can
/// report them.
/// </para>
/// </summary>
public class HandbookEntityConfigurationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"HandbookConfig_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Entity_maps_to_the_handbook_table()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(HandbookChunk));

        Assert.NotNull(entity);
        Assert.Equal("HandbookChunk", entity!.GetTableName());
    }

    [Fact]
    public void Source_position_is_unique_so_the_upsert_is_idempotent()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(HandbookChunk))!;
        var unique = entity.GetIndexes().Single(i => i.IsUnique);

        Assert.Equal(
            new[] { "SourceKey", "Ordinal" },
            unique.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Audience_defaults_to_staff()
    {
        using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(HandbookChunk))!.FindProperty("Audience")!;

        Assert.Equal("staff", property.GetDefaultValue());
    }

    [Fact]
    public void Is_active_defaults_to_true()
    {
        using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(HandbookChunk))!.FindProperty("IsActive")!;

        Assert.Equal(true, property.GetDefaultValue());
    }

    [Fact]
    public void Neither_search_column_is_part_of_the_ef_model()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(HandbookChunk))!;

        Assert.Null(entity.FindProperty("embedding"));
        Assert.Null(entity.FindProperty("SearchVector"));
    }
}
