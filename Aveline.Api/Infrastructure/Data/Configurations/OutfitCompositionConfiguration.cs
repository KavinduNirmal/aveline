using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class OutfitCompositionConfiguration : IEntityTypeConfiguration<OutfitComposition>
{
    public void Configure(EntityTypeBuilder<OutfitComposition> builder)
    {
        builder.ToTable("Outfit_Compositions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.Occasion)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.TotalPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.StyleNotes)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(i => i.OutfitCompositionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.OrgId, x.CustomerId });
    }
}
