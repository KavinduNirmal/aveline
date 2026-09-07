using Aveline.Api.Modules.Attendance.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("TimeEntries");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Source)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.ClockInAt)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired();

        // Lookups by user and by boutique.
        builder.HasIndex(t => new { t.UserId, t.ClockInAt });
        builder.HasIndex(t => t.OrganizationId);

        // Enforce a single open shift per user.
        builder.HasIndex(t => t.UserId)
            .IsUnique()
            .HasFilter("\"ClockOutAt\" IS NULL");

        builder.HasOne(t => t.User)
            .WithMany(u => u.TimeEntries)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Organization)
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
