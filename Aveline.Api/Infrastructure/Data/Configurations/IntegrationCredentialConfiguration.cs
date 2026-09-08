using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class IntegrationCredentialConfiguration : IEntityTypeConfiguration<IntegrationCredential>
{
    public void Configure(EntityTypeBuilder<IntegrationCredential> builder)
    {
        builder.ToTable("IntegrationCredentials");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.IntegrationType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.EncryptedValue)
            .IsRequired();

        builder.Property(c => c.Metadata)
            .HasColumnType("jsonb");

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.HasOne(c => c.Organization)
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // One credential per organization per integration type.
        builder.HasIndex(c => new { c.OrganizationId, c.IntegrationType })
            .IsUnique();

        // For listing all integrations of an organization.
        builder.HasIndex(c => c.OrganizationId);
    }
}
