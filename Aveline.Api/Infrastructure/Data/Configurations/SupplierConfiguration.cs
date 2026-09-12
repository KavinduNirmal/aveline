using System.Collections.Generic;
using System.Text.Json;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Name);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.SupplierName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ContactInfo)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null)
            );

        builder.Property(x => x.ContactEmail)
            .HasMaxLength(255);

        builder.Property(x => x.ContactPhone)
            .HasMaxLength(50);

        builder.Property(x => x.ApiEndpoint)
            .HasMaxLength(500);

        builder.Property(x => x.MinimumOrder)
            .HasPrecision(12, 2);

        builder.Property(x => x.DeliveryTimeDays);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.OrgId).HasDatabaseName("idx_suppliers_org");
    }
}
