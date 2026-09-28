using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Privacy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>EF Core mapping for the durable data-subject-request log (plan §3.3).</summary>
public class DataSubjectRequestConfiguration : IEntityTypeConfiguration<DataSubjectRequest>
{
    public void Configure(EntityTypeBuilder<DataSubjectRequest> builder)
    {
        builder.ToTable("DataSubjectRequests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OrganizationId)
            .IsRequired();

        builder.Property(r => r.Kind)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(r => r.PhoneHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.RequestedAt)
            .IsRequired();

        // Counts, never content. jsonb so an operator query can reach into the counts.
        builder.Property(r => r.ResultJson)
            .HasColumnType("jsonb");

        builder.Property(r => r.FailureReason)
            .HasMaxLength(512);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(64);

        // The organisation is a hard boundary: deleting it can never drag the request log with it,
        // and the log is the evidence a request happened.
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The request is about the customer, but it must survive them: SET NULL leaves the phone
        // fingerprint as the only link, which is exactly what a repeat request matches on (Q-4).
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        // The idempotency contract: one row per caller key per kind per organisation.
        builder.HasIndex(r => new { r.OrganizationId, r.Kind, r.IdempotencyKey })
            .IsUnique();

        // A repeat request after erasure matches on the fingerprint.
        builder.HasIndex(r => new { r.OrganizationId, r.PhoneHash });

        // The cross-org view an erasure needs, and the surviving link to the former customer.
        builder.HasIndex(r => r.CustomerId);
    }
}
