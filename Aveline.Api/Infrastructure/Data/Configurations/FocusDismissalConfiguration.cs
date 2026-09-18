using Aveline.Api.Modules.Home.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class FocusDismissalConfiguration : IEntityTypeConfiguration<FocusDismissal>
{
    public void Configure(EntityTypeBuilder<FocusDismissal> builder)
    {
        builder.ToTable("Focus_Dismissals");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.OrganizationId).IsRequired();
        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.Domain).IsRequired().HasMaxLength(32);
        builder.Property(d => d.SourceKey).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Decision).IsRequired().HasMaxLength(32);
        builder.Property(d => d.ContentHash).HasMaxLength(64);
        builder.Property(d => d.Note).HasMaxLength(500);
        builder.Property(d => d.DismissedAtUtc).IsRequired();

        // The feed's read path: one user's dismissals for one organization.
        builder.HasIndex(d => new { d.OrganizationId, d.UserId });

        // Re-dismissing the same fact with the same content is the same end state,
        // so the write is an upsert rather than a duplicate row.
        builder.HasIndex(d => new { d.OrganizationId, d.UserId, d.Domain, d.SourceKey })
            .IsUnique();

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
