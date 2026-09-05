using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        builder.ToTable("OrganizationInvitations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(i => i.TokenHash)
            .IsUnique();

        builder.HasIndex(i => i.OrganizationId);

        builder.HasIndex(i => new { i.OrganizationId, i.AcceptedAt });

        builder.Property(i => i.RecipientEmail)
            .HasMaxLength(255);

        builder.Property(i => i.BoutiqueRole)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.ExpiresAt)
            .IsRequired();

        builder.Property(i => i.CreatedAt)
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .IsRequired();

        builder.HasOne(i => i.InvitedBy)
            .WithMany()
            .HasForeignKey(i => i.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Recipient)
            .WithMany()
            .HasForeignKey(i => i.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
