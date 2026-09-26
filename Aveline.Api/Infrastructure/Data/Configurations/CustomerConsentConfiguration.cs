using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerConsentConfiguration : IEntityTypeConfiguration<CustomerConsent>
{
    public void Configure(EntityTypeBuilder<CustomerConsent> builder)
    {
        builder.ToTable("CustomerConsent");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.OrganizationId)
            .IsRequired();

        builder.Property(c => c.CustomerId)
            .IsRequired();

        builder.Property(c => c.ConsentStatus)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue(ConsentStatuses.Pending);

        builder.Property(c => c.ConsentSource)
            .HasMaxLength(16);

        builder.Property(c => c.DisclosureVersion)
            .HasMaxLength(32);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.HasOne(c => c.Customer)
            .WithMany(cu => cu.Consents)
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Organization)
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // One consent row per customer per org.
        builder.HasIndex(c => new { c.OrganizationId, c.CustomerId }).IsUnique();

        // Every consent metric is "count by status within an org".
        builder.HasIndex(c => new { c.OrganizationId, c.ConsentStatus });
    }
}
