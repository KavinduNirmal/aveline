using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("AiUsageRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.WorkflowId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.Provider)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.Model)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.ActualCostUsd)
            .HasPrecision(18, 8);

        builder.Property(r => r.BlossomUnits)
            .HasPrecision(18, 4);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // Index for querying organization usage history by date
        builder.HasIndex(r => new { r.OrganizationId, r.CreatedAt });
    }
}

public class UsageAccountConfiguration : IEntityTypeConfiguration<UsageAccount>
{
    public void Configure(EntityTypeBuilder<UsageAccount> builder)
    {
        builder.ToTable("UsageAccounts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.MonthlyBlossomLimit)
            .HasPrecision(18, 4);

        builder.Property(a => a.BlossomUsed)
            .HasPrecision(18, 4);

        builder.Property(a => a.BlossomRemaining)
            .HasPrecision(18, 4);

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(a => a.PeriodStart)
            .IsRequired();

        builder.Property(a => a.PeriodEnd)
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .IsRequired();

        // Unique index ensures one active ledger row per organisation per billing period
        builder.HasIndex(a => new { a.OrganizationId, a.PeriodStart })
            .IsUnique();
    }
}
