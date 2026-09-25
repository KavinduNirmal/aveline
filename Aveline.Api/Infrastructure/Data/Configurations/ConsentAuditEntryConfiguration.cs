using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>EF Core mapping for the append-only consent history (plan §3.2).</summary>
public class ConsentAuditEntryConfiguration : IEntityTypeConfiguration<ConsentAuditEntry>
{
    public void Configure(EntityTypeBuilder<ConsentAuditEntry> builder)
    {
        builder.ToTable("ConsentAuditEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OrganizationId)
            .IsRequired();

        builder.Property(e => e.CustomerId)
            .IsRequired();

        builder.Property(e => e.Action)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(e => e.PreviousStatus)
            .HasMaxLength(16);

        builder.Property(e => e.NewStatus)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.Source)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.ActorKind)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.ActorRef)
            .HasMaxLength(128);

        // jsonb, defaulting to an empty object: evidence is identifiers, never content.
        builder.Property(e => e.EvidenceJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValue("{}");

        builder.Property(e => e.IpHash)
            .HasMaxLength(64);

        builder.Property(e => e.UserAgent)
            .HasMaxLength(256);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        // The organisation is a hard boundary: deleting it can never drag consent history with it.
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The history is *about* the customer, so erasure cascades (plan §3.2, Q-4).
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The staff/agent timeline for one customer in one org.
        builder.HasIndex(e => new { e.OrganizationId, e.CustomerId, e.CreatedAt })
            .IsDescending(false, false, true);

        // The cross-org customer view an erasure request needs.
        builder.HasIndex(e => new { e.CustomerId, e.CreatedAt })
            .IsDescending(false, true);
    }
}
