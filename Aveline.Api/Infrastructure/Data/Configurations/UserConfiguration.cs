using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.ClerkId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(u => u.ClerkId)
            .IsUnique();

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(u => u.Email);

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.DisplayName)
            .HasMaxLength(200);

        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(u => u.UserRole)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(u => u.OrganizationRole)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(u => u.HasCompletedOnboarding)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.AccountState)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(AccountState.OnboardingPending);

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.PushNotificationsEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.ContactPreference)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired();

        // Soft delete query filter
        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}
