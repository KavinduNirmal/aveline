using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #227 — EF configuration for the M8 system statistics schema (domain-model.md §8):
/// table names, bounded-varchar enums, the exactly-one-value CHECK constraint and the
/// documented indexes.
/// </summary>
public class SystemStatisticsEntityConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"SystemStatsConfig_{Guid.NewGuid()}")
            .Options);

    private static AppDbContext CreateNpgsqlModelContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options);

    private static IModel DesignTimeModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    [Theory]
    [InlineData(typeof(SystemMetricSample), "SystemMetricSamples")]
    [InlineData(typeof(SystemAlertRule), "SystemAlertRules")]
    [InlineData(typeof(SystemAlert), "SystemAlerts")]
    public void Entity_MapsToExpectedTable(Type entityType, string expectedTable)
    {
        using var context = CreateContext();
        Assert.Equal(expectedTable, context.Model.FindEntityType(entityType)!.GetTableName());
    }

    [Fact]
    public void SystemMetricSample_UsesBigintIdentityPrimaryKey()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(SystemMetricSample))!;
        var id = entity.FindProperty(nameof(SystemMetricSample.Id))!;

        Assert.Equal(typeof(long), id.ClrType);
        Assert.Equal(ValueGenerated.OnAdd, id.ValueGenerated);
        Assert.Equal(
            NpgsqlValueGenerationStrategy.IdentityAlwaysColumn,
            id.GetAnnotations().Single(a => a.Name == "Npgsql:ValueGenerationStrategy").Value);
    }

    [Fact]
    public void SystemMetricSample_DeclaresTheExactlyOneValueCheckConstraint()
    {
        using var context = CreateNpgsqlModelContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(SystemMetricSample))!;
        var check = entity.GetCheckConstraints()
            .SingleOrDefault(c => c.Name == "CK_SystemMetricSamples_Value");

        Assert.NotNull(check);
        Assert.Contains("ValueDecimal", check!.Sql);
        Assert.Contains("ValueBigint", check.Sql);
    }

    [Fact]
    public void SystemMetricSample_IsUniqueOnMetricDimensionsWindow()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(SystemMetricSample))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(SystemMetricSample.MetricName),
                nameof(SystemMetricSample.DimensionHash),
                nameof(SystemMetricSample.WindowStart),
                nameof(SystemMetricSample.WindowSize),
            }));

        Assert.True(index.IsUnique);
        Assert.Equal("IX_SystemMetricSamples_Metric_Dims_Window", index.GetDatabaseName());
    }

    [Fact]
    public void SystemMetricSample_HasTheDocumentedMetricWindowLookupIndex()
    {
        using var context = CreateNpgsqlModelContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(SystemMetricSample))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(SystemMetricSample.MetricName),
                nameof(SystemMetricSample.WindowStart),
            }));

        Assert.Equal("IX_SystemMetricSamples_Metric_Window", index.GetDatabaseName());
        Assert.True(index.IsDescending![1]);
    }

    [Fact]
    public void SystemMetricSample_HasTheDocumentedColumnTypes()
    {
        using var context = CreateNpgsqlModelContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(SystemMetricSample))!;

        Assert.Equal(100, entity.FindProperty(nameof(SystemMetricSample.MetricName))!.GetMaxLength());
        Assert.Equal("jsonb", entity.FindProperty(nameof(SystemMetricSample.DimensionsJson))!.GetColumnType());
        Assert.True(entity.FindProperty(nameof(SystemMetricSample.DimensionHash))!.IsFixedLength());
        Assert.Equal(64, entity.FindProperty(nameof(SystemMetricSample.DimensionHash))!.GetMaxLength());
        Assert.Equal("numeric(18,6)", entity.FindProperty(nameof(SystemMetricSample.ValueDecimal))!.GetColumnType());
        Assert.Equal(24, entity.FindProperty(nameof(SystemMetricSample.Unit))!.GetMaxLength());
        Assert.Equal(8, entity.FindProperty(nameof(SystemMetricSample.WindowSize))!.GetMaxLength());
    }

    [Fact]
    public void SystemAlertRule_UsesVersionSevenIdentifierAndEnumsAsBoundedVarchar()
    {
        using var context = CreateNpgsqlModelContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(SystemAlertRule))!;

        var id = entity.FindProperty(nameof(SystemAlertRule.Id))!;
        Assert.Equal(typeof(Guid), id.ClrType);

        var aggregation = entity.FindProperty(nameof(SystemAlertRule.Aggregation))!;
        Assert.Equal("character varying(16)", aggregation.GetColumnType());
        Assert.Equal(16, aggregation.GetMaxLength());

        var comparison = entity.FindProperty(nameof(SystemAlertRule.ComparisonOperator))!;
        Assert.Equal("character varying(4)", comparison.GetColumnType());
        Assert.Equal(4, comparison.GetMaxLength());

        var severity = entity.FindProperty(nameof(SystemAlertRule.Severity))!;
        Assert.Equal("character varying(16)", severity.GetColumnType());
        Assert.Equal(16, severity.GetMaxLength());
    }

    [Fact]
    public void SystemAlertRule_IsUniqueOnNameAndHasFilteredEnabledIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(SystemAlertRule))!;
        var indexes = entity.GetIndexes().ToArray();

        var unique = indexes.Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(SystemAlertRule.Name) }));
        Assert.True(unique.IsUnique);
        Assert.Equal("IX_SystemAlertRules_Name", unique.GetDatabaseName());

        var enabled = indexes.Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(SystemAlertRule.IsEnabled) }));
        Assert.Equal("IX_SystemAlertRules_Enabled", enabled.GetDatabaseName());
        Assert.Contains("IsEnabled", enabled.GetFilter());
    }

    [Fact]
    public void SystemAlertRule_StoresJsonbTargetRolesAndDimensionFilters()
    {
        using var context = CreateNpgsqlModelContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(SystemAlertRule))!;

        Assert.Equal("jsonb", entity.FindProperty(nameof(SystemAlertRule.TargetRoles))!.GetColumnType());
        Assert.Equal("jsonb", entity.FindProperty(nameof(SystemAlertRule.DimensionFiltersJson))!.GetColumnType());
    }

    [Fact]
    public void SystemAlert_UsesVersionSevenIdentifierAndBoundedVarcharEnums()
    {
        using var context = CreateNpgsqlModelContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(SystemAlert))!;

        Assert.Equal(typeof(Guid), entity.FindProperty(nameof(SystemAlert.Id))!.ClrType);
        Assert.Equal("character varying(16)", entity.FindProperty(nameof(SystemAlert.Severity))!.GetColumnType());
        Assert.Equal(16, entity.FindProperty(nameof(SystemAlert.Severity))!.GetMaxLength());
        Assert.Equal("character varying(16)", entity.FindProperty(nameof(SystemAlert.Status))!.GetColumnType());
        Assert.Equal(16, entity.FindProperty(nameof(SystemAlert.Status))!.GetMaxLength());
        Assert.Equal(200, entity.FindProperty(nameof(SystemAlert.Title))!.GetMaxLength());
        Assert.Equal(2000, entity.FindProperty(nameof(SystemAlert.Detail))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(SystemAlert.ResolutionNote))!.GetMaxLength());
    }

    [Fact]
    public void SystemAlert_HasTheDocumentedIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(SystemAlert))!;
        var indexes = entity.GetIndexes().ToArray();

        Assert.Contains(indexes, i => i.GetDatabaseName() == "IX_SystemAlerts_Status_Fired");
        Assert.Contains(indexes, i => i.GetDatabaseName() == "IX_SystemAlerts_Severity_Status");
        Assert.Contains(indexes, i =>
            i.GetDatabaseName() == "IX_SystemAlerts_Org_Fired"
            && i.GetFilter()?.Contains("OrganizationId") == true);
    }

    [Fact]
    public void DomainIdentifierFactories_GenerateVersionSevenGuids()
    {
        Assert.Equal(7, new SystemAlertRule().Id.Version);
        Assert.Equal(7, new SystemAlert().Id.Version);
    }

    [Fact]
    public void SeededRules_ContainTheTwelveDocumentedRulesWithStableGuids()
    {
        Assert.Equal(12, SystemAlertRuleSeed.Rules.Count);
        Assert.Equal(12, SystemAlertRuleSeed.Rules.Select(r => r.Id).Distinct().Count());
        Assert.Equal(12, SystemAlertRuleSeed.Rules.Select(r => r.Name).Distinct().Count());

        Assert.Contains(SystemAlertRuleSeed.Rules, r =>
            r.Name == "blossom.ledger.drift"
            && r.MetricName == "aveline.blossom.reconciliation.drift"
            && r.Severity == AlertSeverity.Critical);

        Assert.Contains(SystemAlertRuleSeed.Rules, r =>
            r.Name == "api.error.rate"
            && r.MetricName == "aveline.api.error_rate"
            && r.Severity == AlertSeverity.Critical);

        // M-9 closed (Slice 5): Npgsql's meter is registered and the collector emits the
        // saturation ratio as a produced metric, so the rule finally has a producer path.
        Assert.Contains(SystemAlertRuleSeed.Rules, r =>
            r.Name == "db.pool.saturated"
            && r.MetricName == "aveline.db.pool.saturation"
            && r.Aggregation == AlertAggregation.Max
            && r.ComparisonOperator == AlertComparisonOperator.Gt);

        Assert.Contains(SystemAlertRuleSeed.Rules, r =>
            r.Name == "eventbus.failed" && r.MetricName == "aveline.eventbus.failed");
    }
}
