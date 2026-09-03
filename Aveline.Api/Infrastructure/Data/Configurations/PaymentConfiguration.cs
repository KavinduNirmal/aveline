using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.OrderId)
            .IsRequired();

        builder.HasIndex(p => p.OrderId);

        builder.Property(p => p.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(p => p.PaymentType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.PaymentMethod)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Status);

        builder.Property(p => p.GatewayTransactionId)
            .HasMaxLength(100);

        builder.HasIndex(p => p.GatewayTransactionId);

        builder.Property(p => p.PaymentLink)
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt)
            .IsRequired();
    }
}
