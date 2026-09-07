using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("Order_Items");

        builder.HasKey(oi => oi.Id);

        // Multi-tenancy
        builder.Property(oi => oi.OrganizationId)
            .IsRequired();

        builder.HasIndex(oi => oi.OrganizationId);

        builder.HasOne(oi => oi.Organization)
            .WithMany()
            .HasForeignKey(oi => oi.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(oi => oi.OrderId)
            .IsRequired();

        builder.HasIndex(oi => oi.OrderId);

        builder.Property(oi => oi.ItemId)
            .IsRequired();

        builder.HasIndex(oi => oi.ItemId);

        builder.Property(oi => oi.ItemName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(oi => oi.Quantity)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(oi => oi.UnitPrice)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(oi => oi.WholesaleCost)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(oi => oi.TotalPrice)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(oi => oi.CreatedAt)
            .IsRequired();
    }
}
