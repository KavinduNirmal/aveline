using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class InventoryImageConfiguration : IEntityTypeConfiguration<InventoryImage>
{
    public void Configure(EntityTypeBuilder<InventoryImage> builder)
    {
        builder.ToTable("Inventory_Images");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.InventoryItemId)
            .IsRequired();

        builder.Property(x => x.ImageUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.DominantColor)
            .HasMaxLength(50);

        builder.Property(x => x.DetectedFabric)
            .HasMaxLength(100);

        builder.Property(x => x.DetectedPattern)
            .HasMaxLength(100);

        builder.Property(x => x.DetectedStyle)
            .HasMaxLength(100);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.OrgId, x.InventoryItemId });
    }
}
