using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class CustomerMatchConfiguration : IEntityTypeConfiguration<CustomerMatch>
{
    public void Configure(EntityTypeBuilder<CustomerMatch> builder)
    {
        builder.ToTable("Customer_Matches");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrgId)
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.ItemId)
            .IsRequired();

        builder.Property(x => x.MatchScore)
            .IsRequired();

        builder.Property(x => x.Reason)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.OrgId, x.ItemId });
        builder.HasIndex(x => new { x.OrgId, x.CustomerId });
    }
}
