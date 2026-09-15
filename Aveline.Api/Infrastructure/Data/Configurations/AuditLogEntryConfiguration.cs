using Aveline.Api.Modules.Audit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>EF Core mapping for the append-only audit table (domain-model.md §9).</summary>
public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActorKind)
            .IsRequired()
            .HasMaxLength(24);

        builder.Property(e => e.ActorRef)
            .HasMaxLength(128);

        builder.Property(e => e.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.EntityId)
            .IsRequired()
            .HasMaxLength(128);

        // jsonb keeps the snapshots queryable without re-parsing free text.
        builder.Property(e => e.BeforeJson)
            .HasColumnType("jsonb");

        builder.Property(e => e.AfterJson)
            .HasColumnType("jsonb");

        builder.Property(e => e.Reason)
            .HasMaxLength(500);

        builder.Property(e => e.RequestId)
            .HasMaxLength(128);

        builder.Property(e => e.IpHash)
            .HasMaxLength(64);

        builder.Property(e => e.UserAgent)
            .HasMaxLength(300);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => new { e.OrganizationId, e.CreatedAt })
            .IsDescending(false, true);

        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.CreatedAt })
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.ActorUserId, e.CreatedAt })
            .IsDescending(false, true);

        builder.HasIndex(e => new { e.Action, e.CreatedAt })
            .IsDescending(false, true);
    }
}
