using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("MessageAttachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.OrganizationId).IsRequired();
        builder.Property(a => a.ConversationId).IsRequired();
        builder.Property(a => a.UploadedByUserId);
        builder.Property(a => a.StorageProvider).IsRequired().HasMaxLength(32);
        builder.Property(a => a.StorageKey).HasMaxLength(200);

        // The catalog's `bytea` precedent for binary media.
        builder.Property(a => a.ImageData).HasColumnType("bytea");

        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.SizeBytes).IsRequired();
        // Lowercase-hex SHA-256 is exactly 64 characters; nullable so pre-existing rows read back.
        builder.Property(a => a.ContentHash).HasMaxLength(64);
        builder.Property(a => a.Url).IsRequired().HasMaxLength(1000);
        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.BoundAtUtc);

        // The conversation's attachments, for a read or a re-list.
        builder.HasIndex(a => new { a.OrganizationId, a.ConversationId });

        // Dedup lookup per tenant: same bytes, same organization. Never authorise from this
        // index — it is a duplicate detector for observability, not a security boundary.
        builder.HasIndex(a => new { a.OrganizationId, a.ContentHash });

        // The send's binding check, and the message's own blocks.
        builder.HasIndex(a => a.MessageId);

        // The orphan sweep: unbound rows older than the TTL. Filtered, so the index holds only
        // the rows the sweep can ever match.
        builder.HasIndex(a => new { a.MessageId, a.CreatedAtUtc })
            .HasFilter("\"MessageId\" IS NULL");

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(a => a.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
