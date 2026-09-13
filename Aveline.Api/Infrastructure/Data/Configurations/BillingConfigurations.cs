using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
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

        builder.Property(r => r.RoundingMode)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasOne(r => r.Organization)
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index for querying organization usage history by date
        builder.HasIndex(r => new { r.OrganizationId, r.CreatedAt });

        // One-to-one link to the agent workflow run that produced this usage row (FR-5.5).
        builder.HasOne<Modules.Statistics.Models.AgentWorkflowRun>()
            .WithMany()
            .HasForeignKey(r => r.AgentWorkflowRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.AgentWorkflowRunId)
            .IsUnique()
            .HasFilter("\"AgentWorkflowRunId\" IS NOT NULL");
    }
}

public class UsageAccountConfiguration : IEntityTypeConfiguration<UsageAccount>
{
    public void Configure(EntityTypeBuilder<UsageAccount> builder)
    {
        builder.ToTable("UsageAccounts", table =>
        {
            // The projection must always equal the normative balance identity so a bug
            // cannot silently desynchronise it (domain-model.md §4.1).
            table.HasCheckConstraint(
                "CK_UsageAccounts_Balance",
                "\"BlossomRemaining\" = \"MonthlyBlossomLimit\" + \"BlossomGranted\" - \"BlossomAdjusted\" - \"BlossomUsed\"");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.MonthlyBlossomLimit)
            .HasPrecision(18, 4);

        builder.Property(a => a.BlossomUsed)
            .HasPrecision(18, 4);

        builder.Property(a => a.BlossomGranted)
            .HasPrecision(18, 4);

        builder.Property(a => a.BlossomAdjusted)
            .HasPrecision(18, 4);

        builder.Property(a => a.BlossomRemaining)
            .HasPrecision(18, 4);

        builder.Property(a => a.PlanTierSnapshot)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(a => a.PeriodStart)
            .IsRequired();

        builder.Property(a => a.PeriodEnd)
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .IsRequired();

        builder.Property(a => a.ConcurrencyToken)
            .IsRowVersion();

        builder.HasOne(a => a.Organization)
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique index ensures one active ledger row per organisation per billing period
        builder.HasIndex(a => new { a.OrganizationId, a.PeriodStart })
            .IsUnique();

        builder.HasIndex(a => new { a.OrganizationId, a.IsClosed, a.PeriodStart })
            .IsDescending(false, false, true);
    }
}
