using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class SignOffDecisionConfiguration : IEntityTypeConfiguration<SignOffDecision>
{
    public void Configure(EntityTypeBuilder<SignOffDecision> builder)
    {
        builder.ToTable("SignOff_Decisions");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.OrganizationId)
            .IsRequired();

        builder.Property(d => d.ConversationId)
            .IsRequired();

        builder.Property(d => d.MessageId)
            .IsRequired();

        builder.Property(d => d.ContentHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(d => d.Approved)
            .IsRequired();

        builder.Property(d => d.DecidedAt)
            .IsRequired();

        // Tenant isolation + lookups by message.
        builder.HasIndex(d => d.OrganizationId);
        builder.HasIndex(d => d.MessageId);
    }
}
