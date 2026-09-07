using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Aveline.Api.Modules.Admin.Models;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>EF configuration for <see cref="AdminApprovalRequest"/>.</summary>
public class AdminApprovalRequestConfiguration : IEntityTypeConfiguration<AdminApprovalRequest>
{
    public void Configure(EntityTypeBuilder<AdminApprovalRequest> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.ClerkUserId);
        builder.HasIndex(r => r.Status);
        builder.Property(r => r.ClerkUserId).HasMaxLength(255);
        builder.Property(r => r.Email).HasMaxLength(320);
        builder.Property(r => r.FirstName).HasMaxLength(100);
        builder.Property(r => r.LastName).HasMaxLength(100);
        builder.Property(r => r.ReviewedByClerkUserId).HasMaxLength(255);
    }
}
