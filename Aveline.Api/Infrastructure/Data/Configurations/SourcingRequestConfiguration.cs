using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class SourcingRequestConfiguration : IEntityTypeConfiguration<SourcingRequest>
{
    public void Configure(EntityTypeBuilder<SourcingRequest> builder)
    {
        builder.ToTable("Sourcing_Requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.CustomerId);

        builder.Property(x => x.ReferenceImageUrl)
            .HasMaxLength(500);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.TargetPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("pending");

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc);

        builder.HasIndex(x => new { x.OrgId, x.Status });
    }
}
