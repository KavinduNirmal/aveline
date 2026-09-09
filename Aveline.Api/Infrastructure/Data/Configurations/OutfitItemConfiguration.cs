using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class OutfitItemConfiguration : IEntityTypeConfiguration<OutfitItem>
{
    public void Configure(EntityTypeBuilder<OutfitItem> builder)
    {
        builder.ToTable("Outfit_Items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OutfitCompositionId)
            .IsRequired();

        builder.Property(x => x.InventoryItemId)
            .IsRequired();

        builder.Property(x => x.Role)
            .IsRequired()
            .HasMaxLength(50);
    }
}
