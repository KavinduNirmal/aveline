using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class InventoryItemTagConfiguration : IEntityTypeConfiguration<InventoryItemTag>
{
    public void Configure(EntityTypeBuilder<InventoryItemTag> builder)
    {
        builder.ToTable("InventoryItemTags");

        builder.HasKey(it => new { it.ItemId, it.TagId });

        builder.Property(it => it.OrgId)
            .IsRequired();

        builder.HasOne(it => it.Item)
            .WithMany(i => i.ItemTags)
            .HasForeignKey(it => it.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(it => it.Tag)
            .WithMany(t => t.ItemTags)
            .HasForeignKey(it => it.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(it => new { it.OrgId, it.TagId })
            .HasDatabaseName("idx_inventory_item_tags_org_tag");

        builder.HasIndex(it => new { it.OrgId, it.ItemId })
            .HasDatabaseName("idx_inventory_item_tags_org_item");
    }
}
