using Aveline.Api.Modules.Integrations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class InboundMessageLogConfiguration : IEntityTypeConfiguration<InboundMessageLog>
{
    public void Configure(EntityTypeBuilder<InboundMessageLog> builder)
    {
        builder.ToTable("InboundMessageLogs");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Channel)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(m => m.Direction)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(m => m.ExternalId)
            .HasMaxLength(128);

        builder.Property(m => m.From)
            .HasMaxLength(64);

        builder.Property(m => m.To)
            .HasMaxLength(64);

        builder.Property(m => m.Content)
            .HasColumnType("text");

        builder.Property(m => m.ReceivedAt)
            .IsRequired();

        // Tenant isolation + chronological lookups.
        builder.HasIndex(m => new { m.OrganizationId, m.ReceivedAt });
        builder.HasIndex(m => m.OrganizationId);
    }
}
