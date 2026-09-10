using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerPreferenceConfiguration : IEntityTypeConfiguration<CustomerPreference>
{
    public void Configure(EntityTypeBuilder<CustomerPreference> builder)
    {
        builder.ToTable("Customer_Preferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.OrganizationId)
            .IsRequired();

        builder.Property(p => p.CustomerId)
            .IsRequired();

        builder.Property(p => p.PreferenceKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.PreferenceValue)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.IsExplicit)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.Confidence)
            .HasPrecision(3, 2)
            .HasDefaultValue(0.50m);

        builder.Property(p => p.Source)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("conversation");

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .IsRequired();

        builder.HasOne(p => p.Customer)
            .WithMany(c => c.Preferences)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Organization)
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.OrganizationId);
        builder.HasIndex(p => p.CustomerId);
        builder.HasIndex(p => new { p.CustomerId, p.PreferenceKey });
    }
}
