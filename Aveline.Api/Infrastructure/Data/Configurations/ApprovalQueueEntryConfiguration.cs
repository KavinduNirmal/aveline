using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class ApprovalQueueEntryConfiguration : IEntityTypeConfiguration<ApprovalQueueEntry>
{
    public void Configure(EntityTypeBuilder<ApprovalQueueEntry> builder)
    {
        builder.ToTable("Approval_Queue");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.OrderId)
            .IsRequired();

        builder.HasIndex(a => a.OrderId);

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

        builder.Property(a => a.CreatedAt)
            .IsRequired();
    }
}
