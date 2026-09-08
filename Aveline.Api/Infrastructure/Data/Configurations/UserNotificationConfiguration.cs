using Aveline.Api.Modules.Notifications.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("UserNotifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.UserId)
            .IsRequired();

        builder.Property(n => n.NotificationRecordId)
            .IsRequired();

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.HasOne(n => n.Notification)
            .WithMany()
            .HasForeignKey(n => n.NotificationRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // Fast inbox listing + unread count for a user (excluding dismissed).
        builder.HasIndex(n => new { n.UserId, n.DismissedAt, n.ReadAt });
    }
}
