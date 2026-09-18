using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.OrganizationId)
            .IsRequired();

        builder.Property(c => c.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(c => c.ThreadId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.ExternalRef)
            .HasMaxLength(128);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.HasOne(c => c.Organization)
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Messages)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tenant isolation, plus one owner-scoped general Salon per (organization, user) and one
        // organization-shared Salon per (organization, customer, kind) - see ADR-021.
        builder.HasIndex(c => c.OrganizationId);
        builder.HasIndex(c => new { c.OrganizationId, c.OwnerUserId, c.CustomerId, c.Kind });
        builder.HasIndex(c => c.ThreadId).IsUnique();
    }
}
