using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class ApprovalQueueEntryConfiguration : IEntityTypeConfiguration<ApprovalQueueEntry>
{
    public void Configure(EntityTypeBuilder<ApprovalQueueEntry> builder)
    {
        builder.ToTable("Approval_Queue");

        builder.HasKey(a => a.Id);

        // Multi-tenancy
        builder.Property(a => a.OrganizationId)
            .IsRequired();

        builder.HasIndex(a => a.OrganizationId);

        builder.HasOne(a => a.Organization)
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Order FK & Relationship
        builder.Property(a => a.OrderId)
            .IsRequired();

        builder.HasIndex(a => a.OrderId);

        builder.HasOne(a => a.Order)
            .WithMany(o => o.Approvals)
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Property(a => a.ApprovalType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(a => a.Status);

        builder.Property(a => a.ThresholdExceeded)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(a => a.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.DecisionComment)
            .HasMaxLength(1000);

        // LangGraph Checkpoint & Salon Thread Link (ADR-016)
        builder.Property(a => a.ThreadId)
            .HasMaxLength(64);

        builder.HasIndex(a => a.ThreadId);

        builder.Property(a => a.ConversationId);

        builder.HasIndex(a => a.ConversationId);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        // Reviewer / Decider User FK
        builder.Property(a => a.DecidedBy);

        builder.HasIndex(a => a.DecidedBy);

        builder.HasOne(a => a.DecidedByUser)
            .WithMany()
            .HasForeignKey(a => a.DecidedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
