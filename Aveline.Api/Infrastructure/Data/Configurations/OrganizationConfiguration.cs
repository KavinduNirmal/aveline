using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(o => o.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(o => o.Slug)
            .IsUnique();

        builder.Property(o => o.ClerkOrgId)
            .HasMaxLength(64);

        builder.HasIndex(o => o.ClerkOrgId)
            .IsUnique();

        builder.HasIndex(o => o.OwnerUserId);

        builder.Property(o => o.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(o => o.Address)
            .HasMaxLength(500);

        builder.Property(o => o.PhoneNumber)
            .HasMaxLength(50);

        builder.Property(o => o.Description)
            .HasMaxLength(1000);

        builder.Property(o => o.LogoUrl)
            .HasMaxLength(1000);

        builder.Property(o => o.PlanTier)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(PlanTier.Seed);

        builder.Property(o => o.BrandVoice)
            .HasMaxLength(500);

        builder.Property(o => o.BusinessRules)
            .HasMaxLength(1000);

        builder.Property(o => o.PreferredColorsFabrics)
            .HasMaxLength(1000);

        builder.Property(o => o.CustomerPreferences)
            .HasMaxLength(1000);

        builder.Property(o => o.OnboardingStep)
            .IsRequired()
            .HasDefaultValue(2);

        builder.Property(o => o.HasCompletedOnboarding)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.UpdatedAt)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Memberships)
            .WithOne(m => m.Organization)
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Invitations)
            .WithOne(i => i.Organization)
            .HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
