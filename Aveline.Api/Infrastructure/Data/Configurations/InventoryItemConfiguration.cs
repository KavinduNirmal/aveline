using System.Collections.Generic;
using System.Text.Json;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items");

        builder.HasKey(i => i.Id);

        builder.Ignore(i => i.Quantity);

        builder.Property(i => i.OrgId)
            .IsRequired();

        builder.Property(i => i.ItemName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.Color)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.Fabric)
            .HasMaxLength(100);

        builder.Property(i => i.Style)
            .HasMaxLength(100);

        builder.Property(i => i.Sizes)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
            );

        builder.Property(i => i.Price)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(i => i.Cost)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(i => i.StockQuantity)
            .IsRequired();

        builder.Property(i => i.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("available");

        builder.Property(i => i.ImageUrl)
            .HasMaxLength(500);

        builder.Property(i => i.Sku)
            .HasMaxLength(100);

        builder.Property(i => i.Description)
            .HasMaxLength(2000);

        builder.Property(i => i.Metadata)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)
            );

        builder.Property(i => i.DeletedAt);

        builder.Property(i => i.CreatedBy);

        builder.Property(i => i.CreatedAtUtc)
            .IsRequired();

        builder.Property(i => i.UpdatedAtUtc);

        builder.HasIndex(i => i.OrgId).HasDatabaseName("idx_inventory_org");
        builder.HasIndex(i => i.Category).HasDatabaseName("idx_inventory_category");
        builder.HasIndex(i => i.Color).HasDatabaseName("idx_inventory_color");
        builder.HasIndex(i => i.Status).HasDatabaseName("idx_inventory_status");
        builder.HasIndex(i => new { i.OrgId, i.Status, i.DeletedAt, i.Category, i.Color });

        builder.HasMany(i => i.Images)
            .WithOne(img => img.Item)
            .HasForeignKey(img => img.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.CustomerMatches)
            .WithOne(m => m.Item)
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
