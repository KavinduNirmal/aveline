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

        // Multi-tenancy
        builder.Property(p => p.OrganizationId)
            .IsRequired();

        builder.HasIndex(p => p.OrganizationId);

        builder.HasOne(p => p.Organization)
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // Phase 9 (plan §9.7): this index was non-unique, so the spec's "idempotent by transaction
        // id" was enforced only as "already-confirmed returns early"
        // (docs/reports/PR-290-slice3-review.md:269). It is unique now, and deliberately **not**
        // filtered to exclude nulls: PostgreSQL already permits many nulls in a unique index, and an
        // explicitly NULLS DISTINCT index would read as a claim about duplicate handling that no
        // database in this project needs. A duplicate non-null provider reference is a rejected
        // write rather than a second row.
        builder.HasIndex(p => p.GatewayTransactionId)
            .IsUnique();

        // The intent pointer the confirmation route polls (Phase 9). Nullable and indexed, because a
        // counter payment has no intent and the lookup is by tenant + id.
        builder.HasIndex(p => p.PaymentIntentId);

        builder.HasOne(p => p.PaymentIntent)
            .WithMany()
            .HasForeignKey(p => p.PaymentIntentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.PaymentLink)
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt)
            .IsRequired();
    }
}
