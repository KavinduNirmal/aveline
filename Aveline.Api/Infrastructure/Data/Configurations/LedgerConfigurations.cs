using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>EF Core mapping for the append-only entitlement ledger (domain-model.md §4.2).</summary>
public class BlossomLedgerEntryConfiguration : IEntityTypeConfiguration<BlossomLedgerEntry>
{
    public void Configure(EntityTypeBuilder<BlossomLedgerEntry> builder)
    {
        builder.ToTable("BlossomLedgerEntries", table =>
        {
            table.HasCheckConstraint("CK_BlossomLedgerEntries_Delta", "\"BlossomDelta\" <> 0");
            table.HasCheckConstraint("CK_BlossomLedgerEntries_Reason", "length(\"Reason\") >= 10");
            table.HasCheckConstraint(
                "CK_BlossomLedgerEntries_Expiry",
                "\"ExpiresAt\" IS NULL OR \"ExpiresAt\" > \"CreatedAt\"");
        });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.EntryType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(entry => entry.SourceKind)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(entry => entry.BlossomDelta)
            .HasPrecision(18, 4);

        builder.Property(entry => entry.BlossomBalanceAfter)
            .HasPrecision(18, 4);

        builder.Property(entry => entry.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entry => entry.SourceRef)
            .HasMaxLength(128);

        builder.Property(entry => entry.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(entry => entry.IdempotencyScope)
            .HasMaxLength(64);

        builder.Property(entry => entry.CreatedAt)
            .IsRequired();

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(entry => entry.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UsageAccount>()
            .WithMany()
            .HasForeignKey(entry => entry.UsageAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new { entry.OrganizationId, entry.CreatedAt })
            .IsDescending(false, true);

        builder.HasIndex(entry => new { entry.OrganizationId, entry.EntryType, entry.CreatedAt })
            .IsDescending(false, false, true);

        builder.HasIndex(entry => entry.ExpiresAt)
            .HasFilter("\"ExpiresAt\" IS NOT NULL");

        builder.HasIndex(entry => new { entry.OrganizationId, entry.IdempotencyScope, entry.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}

/// <summary>EF Core mapping for the idempotency replay store (domain-model.md §4.3).</summary>
public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(record => record.Id);

        builder.Property(record => record.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(record => record.Endpoint)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(record => record.HttpMethod)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(record => record.RequestHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(record => record.ResponseBodyJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(record => record.CreatedAt)
            .IsRequired();

        builder.Property(record => record.ExpiresAt)
            .IsRequired();

        builder.HasIndex(record => new { record.OrganizationId, record.Endpoint, record.IdempotencyKey })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasIndex(record => record.ExpiresAt);
    }
}
