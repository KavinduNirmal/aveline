using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Privacy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>EF Core mapping for the consent tombstone that survives erasure (plan §15 Q-4).</summary>
public class PrivacyErasureTombstoneConfiguration : IEntityTypeConfiguration<PrivacyErasureTombstone>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureTombstone> builder)
    {
        builder.ToTable("PrivacyErasureTombstones");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.OrganizationId)
            .IsRequired();

        builder.Property(t => t.PhoneHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(t => t.ErasedAt)
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // One tombstone per number per organisation; a repeat erasure updates rather than duplicates.
        builder.HasIndex(t => new { t.OrganizationId, t.PhoneHash })
            .IsUnique();
    }
}
