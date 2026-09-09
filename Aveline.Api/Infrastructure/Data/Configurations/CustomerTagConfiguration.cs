using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerTagConfiguration : IEntityTypeConfiguration<CustomerTag>
{
    public void Configure(EntityTypeBuilder<CustomerTag> builder)
    {
        builder.ToTable("Customer_Tags");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.OrganizationId)
            .IsRequired();

        builder.Property(t => t.CustomerId)
            .IsRequired();

        builder.Property(t => t.Tag)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.HasOne(t => t.Customer)
            .WithMany(c => c.Tags)
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Organization)
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.OrganizationId);
        builder.HasIndex(t => t.CustomerId);
        builder.HasIndex(t => new { t.CustomerId, t.Tag }).IsUnique();
    }
}
