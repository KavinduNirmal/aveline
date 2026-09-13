using System.Collections.Generic;
using System.Text.Json;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class InventoryImageConfiguration : IEntityTypeConfiguration<InventoryImage>
{
    public void Configure(EntityTypeBuilder<InventoryImage> builder)
    {
        builder.ToTable("inventory_images");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.ItemId)
            .IsRequired();

        builder.Property(x => x.ImageUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.IsPrimary)
            .HasDefaultValue(false);

        builder.Property(x => x.DominantColor)
            .HasMaxLength(50);

        builder.Property(x => x.DetectedFabric)
            .HasMaxLength(100);

        builder.Property(x => x.DetectedPattern)
            .HasMaxLength(100);

        builder.Property(x => x.DetectedStyle)
            .HasMaxLength(100);

        builder.Property(x => x.AnalysisResult)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null)
            );

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.ItemId).HasDatabaseName("idx_images_item");
        builder.HasIndex(x => x.OrgId).HasDatabaseName("idx_images_org");
        builder.HasIndex(x => new { x.OrgId, x.ItemId });
    }
}
