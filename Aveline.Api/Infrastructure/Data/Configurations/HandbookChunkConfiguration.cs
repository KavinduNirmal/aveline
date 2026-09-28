using Aveline.Api.Modules.Handbook.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class HandbookChunkConfiguration : IEntityTypeConfiguration<HandbookChunk>
{
    public void Configure(EntityTypeBuilder<HandbookChunk> builder)
    {
        builder.ToTable("HandbookChunk");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.SourceKey)
            .IsRequired()
            .HasMaxLength(160);

        builder.Property(c => c.SourceKind)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("company");

        builder.Property(c => c.SourceTitle)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.SourceUrl)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(c => c.HeadingPath)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(c => c.Anchor)
            .HasMaxLength(160);

        builder.Property(c => c.Content)
            .IsRequired();

        builder.Property(c => c.ContentHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.Audience)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue("staff");

        builder.Property(c => c.Ordinal)
            .IsRequired();

        builder.Property(c => c.TagsJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("{}");

        builder.Property(c => c.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        // NOTE: neither search column is part of the EF model (the in-memory test provider cannot
        // map pgvector's `vector` or PostgreSQL's `tsvector`). They are created by the migration
        // via raw SQL and accessed by HandbookRepository through raw SQL:
        //   * `embedding vector(1536)`            - HNSW vector_cosine_ops index (the dense leg)
        //   * `SearchVector tsvector` (GENERATED) - GIN index (the lexical leg)
        // See ADR-017 for the precedent and ADR-025 for the hybrid decision.

        // The upsert key: one chunk per (source, position), so a re-seed is idempotent.
        builder.HasIndex(c => new { c.SourceKey, c.Ordinal }).IsUnique();
        builder.HasIndex(c => c.SourceKind);
        builder.HasIndex(c => c.Audience);
        builder.HasIndex(c => c.ContentHash);
    }
}
