using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerMemoryConfiguration : IEntityTypeConfiguration<CustomerMemory>
{
    public void Configure(EntityTypeBuilder<CustomerMemory> builder)
    {
        builder.ToTable("CustomerMemory");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.OrganizationId)
            .IsRequired();

        builder.Property(m => m.CustomerId)
            .IsRequired();

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(2000);

        // The normalised statement the store's uniqueness rule is stated over (gap A3). It is
        // derived from Content on every write, never accepted from a caller, and it is what the
        // unique index below is built on - so the "same note" rule holds under two concurrent
        // writers and not merely under whichever thread read first.
        builder.Property(m => m.ContentKey)
            .IsRequired()
            .HasMaxLength(2000);

        // NOTE: the two search columns - the pgvector `embedding vector(1536)` column with its HNSW
        // cosine index (dense leg) and the generated `SearchVector tsvector` with its partial GIN
        // index (lexical leg) - are intentionally NOT part of the EF model (the in-memory test
        // provider cannot map the pgvector or tsvector types). They are created by the migrations
        // via raw SQL and accessed by CustomerMemoryRepository through raw SQL. See ADR-017, ADR-025.

        builder.Property(m => m.Category)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("fact");

        builder.Property(m => m.Source)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("conversation");

        builder.Property(m => m.IsExplicit)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.Confidence)
            .HasPrecision(3, 2)
            .HasDefaultValue(0.50m);

        builder.Property(m => m.MetadataJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("{}");

        // Null for a note that stays true; set for a dated one (gap A4). The search filters on it,
        // so the index below is what keeps that filter from being a scan.
        builder.Property(m => m.ExpiresAt);

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.Property(m => m.UpdatedAt)
            .IsRequired();

        builder.HasOne(m => m.Customer)
            .WithMany(c => c.Memories)
            .HasForeignKey(m => m.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Organization)
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.OrganizationId);
        builder.HasIndex(m => m.CustomerId);
        builder.HasIndex(m => m.Category);

        // Filters over a null on both sides (a note with no expiry is always live). The API surface
        // is "live notes for this customer", so the index is scoped the same way.
        builder.HasIndex(m => new { m.OrganizationId, m.CustomerId, m.ExpiresAt });

        // One live note per statement per customer (gap A3). Soft-deleted rows are excluded so a
        // withdrawn note can be stated again, and the search query filters `DeletedAt IS NULL`, so
        // the index predicate matches what the application considers present.
        builder.HasIndex(m => new { m.OrganizationId, m.CustomerId, m.ContentKey })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        // Soft delete query filter.
        builder.HasQueryFilter(m => m.DeletedAt == null);
    }
}
