using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class NotificationRecordConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> builder)
    {
        builder.ToTable("NotificationRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OrganizationId)
            .IsRequired();

        builder.HasIndex(r => r.OrganizationId);

        builder.HasOne(r => r.Organization)
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Title)
            .IsRequired();

        builder.Property(r => r.Body)
            .IsRequired();

        builder.Property(r => r.DataJson)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasMany(r => r.Deliveries)
            .WithOne(d => d.Notification)
            .HasForeignKey(d => d.NotificationRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
