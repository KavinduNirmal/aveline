using Aveline.Api.Modules.Revenue.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for the append-only revenue journal (S-50). Modelled on
/// <see cref="BlossomLedgerEntryConfiguration"/> so the two journals are configured the same way:
/// enum-as-string, an explicit precision, a named check constraint declared in the model, and a
/// **filtered unique** index as the dedup identity.
/// </summary>
public class IncomeLedgerEntryConfiguration : IEntityTypeConfiguration<IncomeLedgerEntry>
{
    public void Configure(EntityTypeBuilder<IncomeLedgerEntry> builder)
    {
        builder.ToTable("IncomeLedgerEntries", table =>
        {
            // `Amount` is stored positive and the sign comes from `Kind`, so a non-positive amount
            // is a bug. Declared here rather than only in the migration so its name is assertable.
            table.HasCheckConstraint("CK_IncomeLedgerEntries_Amount", "\"Amount\" > 0");
        });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(entry => entry.SourceKind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(entry => entry.ChargeBasis)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.Currency)
            .HasMaxLength(3)
            .IsRequired();

        // Money precision, deliberately `(18,2)` rather than the Blossom ledger's `(18,4)`: the
        // unit here is currency, and a half-cent must not be able to survive a round trip.
        builder.Property(entry => entry.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entry => entry.SourceRef)
            .HasMaxLength(128);

        builder.Property(entry => entry.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(entry => entry.IdempotencyScope)
            .HasMaxLength(64);

        builder.Property(entry => entry.OccurredAt)
            .IsRequired();

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(entry => entry.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The dedup identity. Partial on a non-null `SourceRef`, because a row without one has no
        // identity to dedupe on and two such rows must not collide with each other.
        builder.HasIndex(entry => new { entry.SourceKind, entry.SourceRef })
            .IsUnique()
            .HasFilter("\"SourceRef\" IS NOT NULL");

        // The three read shapes S-50 needs: a window scan, an organization's window, and a kind
        // breakdown. All descending on `OccurredAt` because every read is "most recent first".
        builder.HasIndex(entry => entry.OccurredAt)
            .IsDescending(true);

        builder.HasIndex(entry => new { entry.OrganizationId, entry.OccurredAt })
            .IsDescending(false, true);

        builder.HasIndex(entry => new { entry.Kind, entry.OccurredAt })
            .IsDescending(false, true);
    }
}
