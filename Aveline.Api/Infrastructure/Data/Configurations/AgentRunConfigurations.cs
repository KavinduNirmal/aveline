using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class AgentWorkflowRunConfiguration : IEntityTypeConfiguration<AgentWorkflowRun>
{
    public void Configure(EntityTypeBuilder<AgentWorkflowRun> builder)
    {
        builder.ToTable("AgentWorkflowRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.WorkflowId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.RequestId)
            .HasMaxLength(128);

        builder.Property(r => r.TriggerKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(r => r.TriggerRef)
            .HasMaxLength(128);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(r => r.AgentsInvolved)
            .IsRequired();

        builder.Property(r => r.PlanTierAtRun)
            .HasMaxLength(32);

        builder.Property(r => r.ActualCostUsd)
            .HasPrecision(18, 8);

        builder.Property(r => r.BlossomUnits)
            .HasPrecision(18, 4);

        builder.Property(r => r.ErrorCode)
            .HasMaxLength(64);

        builder.Property(r => r.ErrorMessage)
            .HasMaxLength(1000);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AgentWorkflowRun>()
            .WithMany()
            .HasForeignKey(r => r.ParentWorkflowRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.WorkflowRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // Idempotent ingestion: one run per (organization, workflow). NULLS NOT DISTINCT so
        // two unattributed runs for the same workflow id still collide.
        builder.HasIndex(r => new { r.OrganizationId, r.WorkflowId })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasIndex(r => new { r.OrganizationId, r.StartedAt })
            .IsDescending(false, true);

        builder.HasIndex(r => new { r.Status, r.StartedAt });

        builder.HasIndex(r => new { r.OrganizationId, r.TriggerKind, r.StartedAt })
            .IsDescending(false, false, true);

        builder.HasIndex(r => r.StartedAt)
            .IsDescending()
            .HasFilter("\"IsUnattributed\"");

        builder.HasIndex(r => r.AgentsInvolved)
            .HasMethod("gin");
    }
}

public class AgentStepRunConfiguration : IEntityTypeConfiguration<AgentStepRun>
{
    public void Configure(EntityTypeBuilder<AgentStepRun> builder)
    {
        builder.ToTable("AgentStepRuns");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.AgentKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.NodeName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.StepKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(s => s.ToolName)
            .HasMaxLength(128);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(s => s.Provider)
            .HasMaxLength(64);

        builder.Property(s => s.Model)
            .HasMaxLength(128);

        builder.Property(s => s.ArgsHash)
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(s => s.ActualCostUsd)
            .HasPrecision(18, 8);

        builder.Property(s => s.ErrorCode)
            .HasMaxLength(64);

        builder.Property(s => s.ErrorMessage)
            .HasMaxLength(1000);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.HasIndex(s => new { s.WorkflowRunId, s.StepIndex, s.AttemptNumber })
            .IsUnique();

        builder.HasIndex(s => new { s.OrganizationId, s.AgentKey, s.StartedAt })
            .IsDescending(false, false, true);

        builder.HasIndex(s => new { s.ToolName, s.StartedAt })
            .IsDescending(false, true)
            .HasFilter("\"ToolName\" IS NOT NULL");

        builder.HasIndex(s => new { s.OrganizationId, s.StartedAt })
            .IsDescending(false, true)
            .HasFilter("\"Status\" = 'Failed'");
    }
}

public class DailyAgentMetricConfiguration : IEntityTypeConfiguration<DailyAgentMetric>
{
    public void Configure(EntityTypeBuilder<DailyAgentMetric> builder)
    {
        builder.ToTable("DailyAgentMetrics");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.AgentKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.Day)
            .IsRequired();

        builder.Property(m => m.ActualCostUsd)
            .HasPrecision(18, 8);

        builder.Property(m => m.BlossomUnits)
            .HasPrecision(18, 4);

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.HasIndex(m => new { m.OrganizationId, m.AgentKey, m.Day })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("IX_DailyAgentMetrics_Org_Agent_Day");
    }
}
