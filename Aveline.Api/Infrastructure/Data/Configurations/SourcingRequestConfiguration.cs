using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class SourcingRequestConfiguration : IEntityTypeConfiguration<SourcingRequest>
{
    public void Configure(EntityTypeBuilder<SourcingRequest> builder)
    {
        builder.ToTable("sourcing_requests");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Category);
        builder.Ignore(x => x.Color);
        builder.Ignore(x => x.Description);
        builder.Ignore(x => x.TargetPrice);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.CustomerId);

        builder.Property(x => x.ReferenceImageUrl)
            .HasMaxLength(500);

        builder.Property(x => x.ItemDescription)
            .HasMaxLength(2000);

        builder.Property(x => x.SupplierId);

        builder.Property(x => x.EstimatedCost)
            .HasPrecision(12, 2);

        builder.Property(x => x.ProposedMarkup)
            .HasPrecision(5, 2);

        builder.Property(x => x.ProposedPrice)
            .HasPrecision(12, 2);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("pending");

        builder.Property(x => x.CreatedBy);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc);

        builder.HasIndex(x => x.OrgId).HasDatabaseName("idx_sourcing_org");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("idx_sourcing_customer");
        builder.HasIndex(x => x.Status).HasDatabaseName("idx_sourcing_status");
    }
}
