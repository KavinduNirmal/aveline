using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerMatchConfiguration : IEntityTypeConfiguration<CustomerMatch>
{
    public void Configure(EntityTypeBuilder<CustomerMatch> builder)
    {
        builder.ToTable("customer_matches");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.MatchScore);
        builder.Ignore(x => x.Reason);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.ItemId)
            .IsRequired();

        builder.Property(x => x.MatchConfidence)
            .HasPrecision(3, 2)
            .IsRequired();

        builder.Property(x => x.MatchReason)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.EmployeeActed)
            .HasDefaultValue(false);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("idx_matches_customer");
        builder.HasIndex(x => x.ItemId).HasDatabaseName("idx_matches_item");
        builder.HasIndex(x => x.OrgId).HasDatabaseName("idx_matches_org");
    }
}
