using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class BusinessRuleConfiguration : IEntityTypeConfiguration<BusinessRule>
{
    public void Configure(EntityTypeBuilder<BusinessRule> builder)
    {
        builder.ToTable("Business_Rules");

        builder.HasKey(b => b.Id);

        // Multi-tenancy
        builder.Property(b => b.OrganizationId)
            .IsRequired();

        builder.HasIndex(b => b.OrganizationId);

        builder.HasOne(b => b.Organization)
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.RuleName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(b => b.RuleType)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(b => b.RuleType);

        builder.Property(b => b.RuleValue)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(b => b.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(b => b.IsActive);

        builder.Property(b => b.Description)
            .HasMaxLength(500);

        builder.Property(b => b.CreatedAt)
            .IsRequired();
    }
}
