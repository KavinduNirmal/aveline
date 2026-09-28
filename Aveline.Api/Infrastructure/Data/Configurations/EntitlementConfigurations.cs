using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class PlanEntitlementConfiguration : IEntityTypeConfiguration<PlanEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanEntitlement> builder)
    {
        builder.ToTable("PlanEntitlements");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PlanTier)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Key)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.ValueType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.ValueDecimal)
            .HasPrecision(18, 4);

        builder.Property(e => e.ValueText)
            .HasMaxLength(200);

        builder.Property(e => e.EffectiveFrom)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => new { e.PlanTier, e.Key, e.EffectiveFrom })
            .IsUnique();
    }
}

public class PlanEntitlementOverrideConfiguration : IEntityTypeConfiguration<PlanEntitlementOverride>
{
    public void Configure(EntityTypeBuilder<PlanEntitlementOverride> builder)
    {
        builder.ToTable("PlanEntitlementOverrides");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Key)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.ValueType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(o => o.ValueDecimal)
            .HasPrecision(18, 4);

        builder.Property(o => o.ValueText)
            .HasMaxLength(200);

        builder.Property(o => o.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.EffectiveFrom)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(o => o.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => new { o.OrganizationId, o.Key, o.EffectiveFrom })
            .IsUnique();
    }
}

public class OrganizationSubscriptionConfiguration : IEntityTypeConfiguration<OrganizationSubscription>
{
    public void Configure(EntityTypeBuilder<OrganizationSubscription> builder)
    {
        builder.ToTable("OrganizationSubscriptions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.PlanTier)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.BillingCycle)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(s => s.PriceLkr)
            .HasPrecision(18, 2);

        builder.Property(s => s.ExternalProvider)
            .HasMaxLength(32);

        builder.Property(s => s.ExternalSubscriptionId)
            .HasMaxLength(128);

        builder.Property(s => s.CurrentPeriodStart)
            .IsRequired();

        builder.Property(s => s.CurrentPeriodEnd)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        // Plan §9.4 F4 dunning state. The nullable columns mean "no window running"; the count is
        // non-nullable so a current subscription reads 0 rather than null.
        builder.Property(s => s.RenewalAttemptCount)
            .HasDefaultValue(0);

        builder.Property(s => s.NextRenewalAttemptAt);

        builder.Property(s => s.DunningStartedAt);

        builder.Property(s => s.ConcurrencyToken)
            .IsRowVersion();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.OrganizationId)
            .IsUnique();

        builder.HasIndex(s => new { s.ExternalProvider, s.ExternalSubscriptionId })
            .IsUnique()
            .HasFilter("\"ExternalSubscriptionId\" IS NOT NULL");
    }
}
