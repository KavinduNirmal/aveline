using Aveline.Api.Modules.Notifications.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class UserDeviceTokenConfiguration : IEntityTypeConfiguration<UserDeviceToken>
{
    public void Configure(EntityTypeBuilder<UserDeviceToken> builder)
    {
        builder.ToTable("UserDeviceTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Token)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(t => t.Platform)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.LastSeenAt)
            .IsRequired();

        // A device token is unique across the platform.
        builder.HasIndex(t => t.Token)
            .IsUnique();

        // For listing a user's active tokens.
        builder.HasIndex(t => t.UserId);
    }
}
