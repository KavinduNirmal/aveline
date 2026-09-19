using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class ConversationReadStateConfiguration : IEntityTypeConfiguration<ConversationReadState>
{
    public void Configure(EntityTypeBuilder<ConversationReadState> builder)
    {
        builder.ToTable("ConversationReadStates");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.OrganizationId).IsRequired();
        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.ConversationId).IsRequired();
        builder.Property(s => s.LastReadAtUtc).IsRequired();

        // One marker per user per conversation: re-reading is an upsert, not a second row.
        builder.HasIndex(s => new { s.OrganizationId, s.UserId, s.ConversationId })
            .IsUnique();

        // The read path, and the aggregate the inbox will consume: one user's markers in one
        // organization.
        builder.HasIndex(s => new { s.OrganizationId, s.UserId });

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
