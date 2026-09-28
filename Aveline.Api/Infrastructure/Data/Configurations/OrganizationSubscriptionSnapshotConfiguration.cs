using Aveline.Api.Modules.Analytics.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF configuration for the business-KPI snapshot table (S-47). Enum columns are stored as
/// strings, matching <c>EntitlementConfigurations</c>'s convention for the subscription table.
/// </summary>
public class OrganizationSubscriptionSnapshotConfiguration
    : IEntityTypeConfiguration<OrganizationSubscriptionSnapshot>
{
    public void Configure(EntityTypeBuilder<OrganizationSubscriptionSnapshot> builder)
    {
        builder.ToTable("OrganizationSubscriptionSnapshots");

        builder.HasKey(snapshot => snapshot.Id);

        builder.Property(snapshot => snapshot.SnapshotDay)
            .IsRequired();

        builder.Property(snapshot => snapshot.PlanTier)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(snapshot => snapshot.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(snapshot => snapshot.BillingCycle)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(snapshot => snapshot.PriceLkr)
            .HasPrecision(18, 2);

        builder.Property(snapshot => snapshot.HasBillingRow)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(snapshot => snapshot.IsBackfilled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(snapshot => snapshot.CreatedAt)
            .IsRequired();

        // The series read: one day's rows grouped by tier.
        builder.HasIndex(snapshot => new { snapshot.SnapshotDay, snapshot.PlanTier })
            .HasDatabaseName("IX_OrgSubscriptionSnapshots_Day_Tier");

        // One row per organization per day, which is what makes recompute-and-replace safe.
        builder.HasIndex(snapshot => new { snapshot.OrganizationId, snapshot.SnapshotDay })
            .IsUnique()
            .HasDatabaseName("IX_OrgSubscriptionSnapshots_Org_Day");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
