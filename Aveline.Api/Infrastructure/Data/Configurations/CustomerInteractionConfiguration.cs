using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerInteractionConfiguration : IEntityTypeConfiguration<CustomerInteraction>
{
    public void Configure(EntityTypeBuilder<CustomerInteraction> builder)
    {
        builder.ToTable("Customer_Interactions");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.OrganizationId)
            .IsRequired();

        builder.Property(i => i.CustomerId)
            .IsRequired();

        builder.Property(i => i.Channel)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("whatsapp");

        builder.Property(i => i.Direction)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue("inbound");

        builder.Property(i => i.MessageContent)
            .HasMaxLength(4000);

        builder.Property(i => i.ParsedIntentJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("{}");

        builder.Property(i => i.CreatedAt)
            .IsRequired();

        builder.HasOne(i => i.Customer)
            .WithMany(c => c.Interactions)
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Organization)
            .WithMany()
            .HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.StaffMember)
            .WithMany()
            .HasForeignKey(i => i.StaffMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.OrganizationId);
        builder.HasIndex(i => i.CustomerId);
        builder.HasIndex(i => i.Channel);
        builder.HasIndex(i => new { i.CustomerId, i.CreatedAt });
    }
}
