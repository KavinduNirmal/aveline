using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class OutfitCompositionConfiguration : IEntityTypeConfiguration<OutfitComposition>
{
    public void Configure(EntityTypeBuilder<OutfitComposition> builder)
    {
        builder.ToTable("outfit_compositions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Occasion)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.TotalPrice)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(x => x.CustomerId);

        builder.Property(x => x.StyleNotes)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasMany(x => x.Items)
            .WithOne(i => i.Outfit)
            .HasForeignKey(i => i.OutfitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.OrgId).HasDatabaseName("idx_outfits_org");
    }
}
