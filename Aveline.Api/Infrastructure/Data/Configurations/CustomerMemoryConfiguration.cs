using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerMemoryConfiguration : IEntityTypeConfiguration<CustomerMemory>
{
    public void Configure(EntityTypeBuilder<CustomerMemory> builder)
    {
        builder.ToTable("Customer_Memory");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.OrganizationId)
            .IsRequired();

        builder.Property(m => m.CustomerId)
            .IsRequired();

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(2000);

        // NOTE: the pgvector `embedding vector(1536)` column and its HNSW cosine index are
        // intentionally NOT part of the EF model (the in-memory test provider cannot map the
        // pgvector type). They are created by the migration via raw SQL and accessed by
        // CustomerMemoryRepository through raw SQL. See ADR-017.

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

        // Soft delete query filter.
        builder.HasQueryFilter(m => m.DeletedAt == null);
    }
}
