using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class DeliveryPlanConfiguration : IEntityTypeConfiguration<DeliveryPlan>
{
    public void Configure(EntityTypeBuilder<DeliveryPlan> builder)
    {
        builder.ToTable("Delivery_Plans");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.OrderId)
            .IsRequired();

        builder.HasIndex(d => d.OrderId)
            .IsUnique();

        builder.Property(d => d.CourierService)
            .HasMaxLength(50);

        builder.Property(d => d.TrackingNumber)
            .HasMaxLength(100);

        builder.HasIndex(d => d.TrackingNumber);

        builder.Property(d => d.DeliveryAddress)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(d => d.RouteOptimized)
            .HasColumnType("jsonb");

        builder.Property(d => d.EstimatedCost)
            .HasPrecision(18, 2);

        builder.Property(d => d.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(d => d.Status);

        builder.Property(d => d.CreatedAt)
            .IsRequired();
    }
}
