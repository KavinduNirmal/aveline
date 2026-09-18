using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(k => k.Prefix)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(k => k.KeyHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(k => k.HashAlgorithm)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(k => k.Scopes)
            .IsRequired();

        builder.Property(k => k.Environment)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(k => k.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(k => k.RevokedReason)
            .HasMaxLength(300);

        builder.Property(k => k.LastUsedIpHash)
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(k => k.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(k => k.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(k => k.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(k => k.Prefix)
            .IsUnique();

        builder.HasIndex(k => new { k.OrganizationId, k.Status });

        builder.HasIndex(k => k.ExpiresAt)
            .HasFilter("\"Status\" = 'Active' AND \"ExpiresAt\" IS NOT NULL");
    }
}
