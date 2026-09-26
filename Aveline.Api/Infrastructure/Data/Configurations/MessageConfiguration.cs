using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConversationId)
            .IsRequired();

        builder.Property(m => m.AuthorKind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.AuthorAgentKey)
            .HasMaxLength(32);

        builder.Property(m => m.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.ContentBlocksJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(m => m.ContentHash)
            .HasMaxLength(64);

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Chronological listing within a conversation. The id is part of the key because
        // Guid.CreateVersion7 makes it monotone, so a tied CreatedAt still has a total order
        // and a page seam can neither duplicate nor skip a row.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt, m.Id });

        // Platform-wide "messages sent per bucket" (business KPIs S-48). The per-conversation
        // index above has no leading CreatedAt, so a day-range filter over every conversation
        // could not use it.
        builder.HasIndex(m => new { m.CreatedAt, m.AuthorUserId })
            .HasDatabaseName("IX_Messages_CreatedAt");

        // One composed message per client key. The filter keeps server-authored messages
        // (which carry no key) out of the index entirely.
        builder.HasIndex(m => new { m.ConversationId, m.ClientMessageId })
            .IsUnique()
            .HasFilter("\"ClientMessageId\" IS NOT NULL");
    }
}
