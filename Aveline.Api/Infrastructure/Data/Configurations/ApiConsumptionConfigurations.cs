using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF configuration for the M7 API consumption statistics schema (domain-model.md §7).
/// The raw <c>ApiRequestLogs</c> table is range-partitioned by hand-edited migration DDL;
/// EF still owns the columns, key and indexes.
/// </summary>
public class ApiRequestMetricConfiguration : IEntityTypeConfiguration<ApiRequestMetric>
{
    public void Configure(EntityTypeBuilder<ApiRequestMetric> builder)
    {
        builder.ToTable("ApiRequestMetrics");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).UseIdentityAlwaysColumn();

        builder.Property(m => m.RouteTemplate)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.HttpMethod)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(m => m.StatusCode)
            .IsRequired();

        builder.Property(m => m.StatusClass)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(m => m.IsThrottled)
            .IsRequired();

        builder.Property(m => m.WindowStart)
            .IsRequired();

        builder.Property(m => m.WindowSize)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(m => m.BucketCounts)
            .IsRequired()
            .HasColumnType("integer[]");

        // The dimensions are a NULLS NOT DISTINCT unique key: two unattributed rows for the
        // same route/method/status/hour still collide, so the upsert stays exact.
        builder.HasIndex(m => new
            {
                m.OrganizationId,
                m.ApiKeyId,
                m.UserId,
                m.RouteTemplate,
                m.HttpMethod,
                m.StatusCode,
                m.WindowStart,
            })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("IX_ApiRequestMetrics_Dimensions");

        builder.HasIndex(m => new { m.OrganizationId, m.WindowStart })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ApiRequestMetrics_Org_Window");

        builder.HasIndex(m => new { m.RouteTemplate, m.WindowStart })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ApiRequestMetrics_Route_Window");

        builder.HasIndex(m => new { m.ApiKeyId, m.WindowStart })
            .IsDescending(false, true)
            .HasFilter("\"ApiKeyId\" IS NOT NULL")
            .HasDatabaseName("IX_ApiRequestMetrics_Key_Window");

        builder.HasIndex(m => m.WindowStart)
            .HasDatabaseName("IX_ApiRequestMetrics_Window");
    }
}

public class ApiRequestLogConfiguration : IEntityTypeConfiguration<ApiRequestLog>
{
    public void Configure(EntityTypeBuilder<ApiRequestLog> builder)
    {
        builder.ToTable("ApiRequestLogs");

        // The partition key must be part of the primary key, hence the composite key.
        builder.HasKey(l => new { l.Id, l.OccurredAt });
        builder.Property(l => l.Id).UseIdentityAlwaysColumn();

        builder.Property(l => l.OccurredAt)
            .IsRequired();

        builder.Property(l => l.RouteTemplate)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(l => l.HttpMethod)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(l => l.StatusCode)
            .IsRequired();

        builder.Property(l => l.RequestId)
            .HasMaxLength(128);

        builder.Property(l => l.ClientIpHash)
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(l => l.UserAgentHash)
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(l => l.ErrorCode)
            .HasMaxLength(64);

        builder.Property(l => l.ResourceType)
            .HasMaxLength(64);

        builder.Property(l => l.ResourceId)
            .HasMaxLength(128);

        builder.HasIndex(l => new { l.OrganizationId, l.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ApiRequestLogs_Org_Occurred");

        builder.HasIndex(l => new { l.ApiKeyId, l.OccurredAt })
            .IsDescending(false, true)
            .HasFilter("\"ApiKeyId\" IS NOT NULL")
            .HasDatabaseName("IX_ApiRequestLogs_Key_Occurred");

        // IX_ApiRequestLogs_Slow and IX_ApiRequestLogs_Errors are both partial indexes on
        // OccurredAt with different predicates. EF Core identifies an index by its property
        // set, so it cannot model two of them; they are created by raw SQL in the hand-edited
        // M7 migration and verified by ApiConsumptionPostgresTests.

        builder.HasIndex(l => l.RequestId)
            .HasFilter("\"RequestId\" IS NOT NULL")
            .HasDatabaseName("IX_ApiRequestLogs_RequestId");
    }
}

public class ApiQuotaUsageConfiguration : IEntityTypeConfiguration<ApiQuotaUsage>
{
    public void Configure(EntityTypeBuilder<ApiQuotaUsage> builder)
    {
        builder.ToTable("ApiQuotaUsage");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.OrganizationId)
            .IsRequired();

        builder.Property(u => u.MetricKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(u => u.PeriodStart)
            .IsRequired();

        builder.Property(u => u.PeriodEnd)
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired();

        builder.HasIndex(u => new { u.OrganizationId, u.ApiKeyId, u.MetricKey, u.PeriodStart })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("IX_ApiQuotaUsage_Scope_Metric_Period");
    }
}
