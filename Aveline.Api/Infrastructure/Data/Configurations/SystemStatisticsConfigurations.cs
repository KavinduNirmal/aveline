using System.Text.Json;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF configuration for the M8 system statistics schema (domain-model.md §8). Enums are
/// persisted as bounded varchars (C-6) and dimensions as jsonb. The exactly-one-value CHECK
/// constraint that EF cannot otherwise express is declared on the table.
/// </summary>
public class SystemMetricSampleConfiguration : IEntityTypeConfiguration<SystemMetricSample>
{
    public void Configure(EntityTypeBuilder<SystemMetricSample> builder)
    {
        builder.ToTable("SystemMetricSamples", table => table.HasCheckConstraint(
            "CK_SystemMetricSamples_Value",
            "(\"ValueDecimal\" IS NOT NULL) <> (\"ValueBigint\" IS NOT NULL)"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).UseIdentityAlwaysColumn();

        builder.Property(s => s.MetricName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.DimensionsJson)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(s => s.DimensionHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(s => s.ValueDecimal)
            .HasPrecision(18, 6);

        builder.Property(s => s.ValueBigint);

        builder.Property(s => s.Unit)
            .IsRequired()
            .HasMaxLength(24);

        builder.Property(s => s.WindowStart)
            .IsRequired();

        builder.Property(s => s.WindowSize)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(s => s.SampledAt)
            .IsRequired();

        // The idempotent-upsert key: one sample per metric/dimensions/window.
        builder.HasIndex(s => new { s.MetricName, s.DimensionHash, s.WindowStart, s.WindowSize })
            .IsUnique()
            .HasDatabaseName("IX_SystemMetricSamples_Metric_Dims_Window");

        builder.HasIndex(s => new { s.MetricName, s.WindowStart })
            .IsDescending(false, true)
            .HasDatabaseName("IX_SystemMetricSamples_Metric_Window");
    }
}

public class SystemAlertRuleConfiguration : IEntityTypeConfiguration<SystemAlertRule>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueConverter<List<string>, string> TargetRolesConverter = new(
        roles => JsonSerializer.Serialize(roles, JsonOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>());

    private static readonly ValueComparer<List<string>> TargetRolesComparer = new(
        (left, right) => left!.SequenceEqual(right!),
        roles => roles.Aggregate(0, (hash, role) => HashCode.Combine(hash, role.GetHashCode())),
        roles => roles.ToList());

    public void Configure(EntityTypeBuilder<SystemAlertRule> builder)
    {
        builder.ToTable("SystemAlertRules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.MetricName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Aggregation)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(r => r.ComparisonOperator)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(4);

        builder.Property(r => r.Threshold)
            .HasPrecision(18, 6);

        builder.Property(r => r.WindowSeconds)
            .IsRequired();

        builder.Property(r => r.Severity)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(r => r.IsEnabled)
            .IsRequired();

        builder.Property(r => r.CooldownSeconds)
            .IsRequired();

        builder.Property(r => r.MaxAlertsPerHour)
            .IsRequired();

        builder.Property(r => r.TargetRoles)
            .IsRequired()
            .HasConversion(TargetRolesConverter, TargetRolesComparer)
            .HasColumnType("jsonb");

        builder.Property(r => r.DimensionFiltersJson)
            .HasColumnType("jsonb");

        builder.Property(r => r.CreatedByUserId)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        builder.HasIndex(r => r.Name)
            .IsUnique()
            .HasDatabaseName("IX_SystemAlertRules_Name");

        builder.HasIndex(r => r.IsEnabled)
            .HasFilter("\"IsEnabled\"")
            .HasDatabaseName("IX_SystemAlertRules_Enabled");
    }
}

public class SystemAlertConfiguration : IEntityTypeConfiguration<SystemAlert>
{
    public void Configure(EntityTypeBuilder<SystemAlert> builder)
    {
        builder.ToTable("SystemAlerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.MetricName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.Severity)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Detail)
            .HasMaxLength(2000);

        builder.Property(a => a.ObservedValue)
            .HasPrecision(18, 6);

        builder.Property(a => a.Threshold)
            .HasPrecision(18, 6);

        builder.Property(a => a.OccurrenceCount)
            .IsRequired();

        builder.Property(a => a.ConsecutiveOkCount)
            .IsRequired();

        builder.Property(a => a.FiredAt)
            .IsRequired();

        builder.Property(a => a.LastObservedAt)
            .IsRequired();

        builder.Property(a => a.ResolutionNote)
            .HasMaxLength(500);

        builder.HasOne(a => a.Rule)
            .WithMany()
            .HasForeignKey(a => a.RuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.Status, a.FiredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_SystemAlerts_Status_Fired");

        builder.HasIndex(a => new { a.Severity, a.Status, a.FiredAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_SystemAlerts_Severity_Status");

        builder.HasIndex(a => new { a.OrganizationId, a.FiredAt })
            .IsDescending(false, true)
            .HasFilter("\"OrganizationId\" IS NOT NULL")
            .HasDatabaseName("IX_SystemAlerts_Org_Fired");
    }
}
