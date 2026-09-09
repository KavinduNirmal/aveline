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

        // Chronological listing within a conversation.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
