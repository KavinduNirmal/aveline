using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for the boutique's own takings journal. Modelled on
/// <see cref="RevenueConfigurations.IncomeLedgerEntryConfiguration"/> so the two journals are
/// configured the same way — enum-as-string, an explicit precision, a named check constraint
/// declared in the model, and a **filtered unique** index as the dedup identity.
/// </summary>
/// <remarks>
/// The two tables are deliberately separate. This one is org-scoped and records what a shop took
/// from its clients; <c>IncomeLedgerEntries</c> records what Aveline billed the shop. A shared table
/// would make every total need a discriminator filter, and one forgotten filter would mix two
/// economies.
/// </remarks>
public class BoutiqueSaleEntryConfiguration : IEntityTypeConfiguration<BoutiqueSaleEntry>
{
    public void Configure(EntityTypeBuilder<BoutiqueSaleEntry> builder)
    {
        builder.ToTable("BoutiqueSaleEntries", table =>
        {
            // `Amount` is stored positive and the sign comes from `Kind`, so a non-positive amount
            // is a bug. Declared here rather than only in the migration so its name is assertable.
            table.HasCheckConstraint(
                "CK_BoutiqueSaleEntries_AmountPositive", "\"Amount\" > 0");
        });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Kind)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(entry => entry.SourceKind)
            .HasConversion<string>()
            .HasMaxLength(24)
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

        // Money precision, deliberately `(18,2)`: the unit is currency, and a half-cent must not be
        // able to survive a round trip.
        builder.Property(entry => entry.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entry => entry.SourceRef)
            .HasMaxLength(200);

        builder.Property(entry => entry.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(entry => entry.IdempotencyScope)
            .HasMaxLength(64);

        builder.Property(entry => entry.OccurredAt)
            .IsRequired();

        builder.Property(entry => entry.RecordedAt)
            .IsRequired();

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(entry => entry.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The dedup identity, scoped to the organization: two shops may legitimately hold the same
        // provider reference and neither may block the other. Partial on a non-null `SourceRef`,
        // because a row without one has no identity to dedupe on.
        builder.HasIndex(entry => new { entry.OrganizationId, entry.SourceKind, entry.SourceRef })
            .IsUnique()
            .HasFilter("\"SourceRef\" IS NOT NULL");

        // The register's read shape and the two breakdowns the Income section needs.
        builder.HasIndex(entry => new { entry.OrganizationId, entry.OccurredAt })
            .IsDescending(false, true);

        builder.HasIndex(entry => new { entry.OrganizationId, entry.Kind, entry.OccurredAt })
            .IsDescending(false, false, true);
    }
}
