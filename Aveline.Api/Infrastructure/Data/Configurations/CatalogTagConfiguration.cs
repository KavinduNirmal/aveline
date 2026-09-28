using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CatalogTagConfiguration : IEntityTypeConfiguration<CatalogTag>
{
    public void Configure(EntityTypeBuilder<CatalogTag> builder)
    {
        builder.ToTable("CatalogTags");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.OrgId)
            .IsRequired();

        builder.Property(t => t.Slug)
            .IsRequired()
            .HasMaxLength(60);

        builder.Property(t => t.Label)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(t => t.ColorHex)
            .HasMaxLength(9);

        builder.Property(t => t.SortOrder)
            .HasDefaultValue(0);

        builder.Property(t => t.IsArchived)
            .HasDefaultValue(false);

        builder.Property(t => t.CreatedAtUtc)
            .IsRequired();

        builder.Property(t => t.UpdatedAtUtc);

        // Unique slug per boutique tenant
        builder.HasIndex(t => new { t.OrgId, t.Slug })
            .IsUnique()
            .HasDatabaseName("idx_catalog_tags_org_slug");

        builder.HasIndex(t => new { t.OrgId, t.IsArchived, t.SortOrder })
            .HasDatabaseName("idx_catalog_tags_org_active_order");
    }
}
