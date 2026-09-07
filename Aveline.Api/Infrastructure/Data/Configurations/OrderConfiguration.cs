using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        // Multi-tenancy
        builder.Property(o => o.OrganizationId)
            .IsRequired();

        builder.HasIndex(o => o.OrganizationId);

        builder.HasOne(o => o.Organization)
            .WithMany()
            .HasForeignKey(o => o.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.CustomerId)
            .IsRequired();

        builder.HasIndex(o => o.CustomerId);

        builder.Property(o => o.CustomerName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.OrderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(o => o.Status);

        builder.Property(o => o.Subtotal)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(o => o.Discount)
            .IsRequired()
            .HasPrecision(18, 2)
            .HasDefaultValue(0.00m);

        builder.Property(o => o.Total)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(o => o.TotalCost)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(o => o.Margin)
            .IsRequired()
            .HasPrecision(5, 4);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // Relationships
        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Payments)
            .WithOne(p => p.Order)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Approvals)
            .WithOne(a => a.Order)
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.DeliveryPlan)
            .WithOne(d => d.Order)
            .HasForeignKey<DeliveryPlan>(d => d.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
