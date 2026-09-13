using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #220 — EF configuration for the M7 API consumption statistics schema
/// (domain-model.md §7). The unique dimension index must be <c>NULLS NOT DISTINCT</c>
/// so the incremental upsert is correct for unattributed rows.
/// </summary>
public class ApiRequestMetricEntityConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ApiConsumptionConfig_{Guid.NewGuid()}")
            .Options);

    private static IModel DesignTimeModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    [Theory]
    [InlineData(typeof(ApiRequestMetric), "ApiRequestMetrics")]
    [InlineData(typeof(ApiRequestLog), "ApiRequestLogs")]
    [InlineData(typeof(ApiQuotaUsage), "ApiQuotaUsage")]
    public void Entity_MapsToExpectedTable(Type entityType, string expectedTable)
    {
        using var context = CreateContext();
        Assert.Equal(expectedTable, context.Model.FindEntityType(entityType)!.GetTableName());
    }

    [Fact]
    public void ApiRequestMetric_UsesBigintIdentityPrimaryKey()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(ApiRequestMetric))!;
        var id = entity.FindProperty(nameof(ApiRequestMetric.Id))!;

        Assert.Equal(typeof(long), id.ClrType);
        Assert.True(id.ValueGenerated == ValueGenerated.OnAdd);
        Assert.Equal(
            NpgsqlValueGenerationStrategy.IdentityAlwaysColumn,
            id.GetAnnotations().Single(a => a.Name == "Npgsql:ValueGenerationStrategy").Value);
    }

    [Fact]
    public void ApiRequestMetric_IsUniqueOnTheDimensionTupleWithNullsNotDistinct()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiRequestMetric))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(ApiRequestMetric.OrganizationId),
                nameof(ApiRequestMetric.ApiKeyId),
                nameof(ApiRequestMetric.UserId),
                nameof(ApiRequestMetric.RouteTemplate),
                nameof(ApiRequestMetric.HttpMethod),
                nameof(ApiRequestMetric.StatusCode),
                nameof(ApiRequestMetric.WindowStart),
            }));

        Assert.True(index.IsUnique);
        Assert.Contains(
            index.GetAnnotations(),
            annotation => annotation.Name.Contains("NullsDistinct") && annotation.Value is false);
    }

    [Fact]
    public void ApiRequestMetric_HasTheDocumentedIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiRequestMetric))!;
        var indexes = entity.GetIndexes().ToArray();

        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(ApiRequestMetric.OrganizationId), nameof(ApiRequestMetric.WindowStart) }));
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(ApiRequestMetric.RouteTemplate), nameof(ApiRequestMetric.WindowStart) }));
        Assert.Contains(indexes, i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(ApiRequestMetric.ApiKeyId), nameof(ApiRequestMetric.WindowStart) })
            && i.GetFilter()?.Contains("ApiKeyId") == true);
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(ApiRequestMetric.WindowStart) }));
    }

    [Fact]
    public void ApiRequestMetric_BucketCountsIsAPostgresIntegerArray()
    {
        // The in-memory provider has no relational type mapping, so the column-type checks
        // build the design-time model against Npgsql (no connection is opened).
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options);
        var entity = DesignTimeModel(context).FindEntityType(typeof(ApiRequestMetric))!;

        Assert.Equal(typeof(int[]), entity.FindProperty(nameof(ApiRequestMetric.BucketCounts))!.ClrType);
        Assert.Equal(
            "integer[]",
            entity.FindProperty(nameof(ApiRequestMetric.BucketCounts))!.GetColumnType());
        Assert.Equal(
            typeof(long),
            entity.FindProperty(nameof(ApiRequestMetric.RequestBytes))!.ClrType);
        Assert.Equal(
            typeof(long),
            entity.FindProperty(nameof(ApiRequestMetric.TotalDurationMs))!.ClrType);
    }

    [Fact]
    public void ApiRequestLog_HasCompositeKeyOnIdAndOccurredAt()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiRequestLog))!;

        Assert.Equal(
            new[] { nameof(ApiRequestLog.Id), nameof(ApiRequestLog.OccurredAt) },
            entity.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void ApiRequestLog_HasTheDocumentedEfModelableIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiRequestLog))!;
        var indexes = entity.GetIndexes().ToArray();

        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(ApiRequestLog.OrganizationId), nameof(ApiRequestLog.OccurredAt) }));
        Assert.Contains(indexes, i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(ApiRequestLog.ApiKeyId), nameof(ApiRequestLog.OccurredAt) })
            && i.GetFilter()?.Contains("ApiKeyId") == true);
        Assert.Contains(indexes, i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ApiRequestLog.RequestId) })
            && i.GetFilter()?.Contains("RequestId") == true);
    }

    [Fact]
    public void ApiRequestLog_HashesAreFixedLengthSha256Columns()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(ApiRequestLog))!;

        foreach (var name in new[] { nameof(ApiRequestLog.ClientIpHash), nameof(ApiRequestLog.UserAgentHash) })
        {
            var property = entity.FindProperty(name)!;
            Assert.Equal(64, property.GetMaxLength());
            Assert.True(property.IsFixedLength());
        }

        Assert.Equal(200, entity.FindProperty(nameof(ApiRequestLog.RouteTemplate))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(ApiRequestLog.RequestId))!.GetMaxLength());
    }

    [Fact]
    public void ApiQuotaUsage_IsUniqueOnScopeMetricAndPeriodWithNullsNotDistinct()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ApiQuotaUsage))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(ApiQuotaUsage.OrganizationId),
                nameof(ApiQuotaUsage.ApiKeyId),
                nameof(ApiQuotaUsage.MetricKey),
                nameof(ApiQuotaUsage.PeriodStart),
            }));

        Assert.True(index.IsUnique);
        Assert.Contains(
            index.GetAnnotations(),
            annotation => annotation.Name.Contains("NullsDistinct") && annotation.Value is false);
    }

    [Fact]
    public void ApiQuotaUsage_GeneratesVersionSevenIdentifiers()
    {
        var usage = new ApiQuotaUsage();
        Assert.Equal(7, usage.Id.Version);
    }
}
