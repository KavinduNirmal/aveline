using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>Issue #209 — EF configuration for the agentic statistics schema (M6).</summary>
public class AgentRunEntityConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"AgentRunConfig_{Guid.NewGuid()}")
            .Options);

    private static IModel DesignTimeModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    [Theory]
    [InlineData(typeof(AgentWorkflowRun), "AgentWorkflowRuns")]
    [InlineData(typeof(AgentStepRun), "AgentStepRuns")]
    public void Entity_MapsToExpectedTable(Type entityType, string expectedTable)
    {
        using var context = CreateContext();
        Assert.Equal(expectedTable, context.Model.FindEntityType(entityType)!.GetTableName());
    }

    [Fact]
    public void WorkflowRun_IsUniquePerOrganizationAndWorkflow()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AgentWorkflowRun))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(AgentWorkflowRun.OrganizationId), nameof(AgentWorkflowRun.WorkflowId) }));

        Assert.True(index.IsUnique);
        Assert.Contains(
            index.GetAnnotations(),
            annotation => annotation.Name.Contains("NullsDistinct") && annotation.Value is false);
    }

    [Fact]
    public void WorkflowRun_HasUnattributedAndAgentIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AgentWorkflowRun))!;

        var unattributed = entity.GetIndexes().Single(i => i.GetFilter()?.Contains("IsUnattributed") == true);
        Assert.Contains("StartedAt", unattributed.Properties.Select(p => p.Name));

        Assert.Contains(entity.GetIndexes(), i => i.Properties.Any(p => p.Name == nameof(AgentWorkflowRun.AgentsInvolved)));

        // Organization + started-at, org + trigger + started-at, and status + started-at.
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(AgentWorkflowRun.OrganizationId), nameof(AgentWorkflowRun.StartedAt) }));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(AgentWorkflowRun.OrganizationId), nameof(AgentWorkflowRun.TriggerKind), nameof(AgentWorkflowRun.StartedAt) }));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(AgentWorkflowRun.Status), nameof(AgentWorkflowRun.StartedAt) }));
    }

    [Fact]
    public void WorkflowRun_PersistsEnumsAsStrings()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(AgentWorkflowRun))!;

        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(AgentWorkflowRun.Status))!.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(AgentWorkflowRun.TriggerKind))!.GetTypeMapping().Converter?.ProviderClrType);
    }

    [Fact]
    public void StepRun_HasUniqueOrderIndexAndFilteredIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AgentStepRun))!;

        var unique = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[]
                {
                    nameof(AgentStepRun.WorkflowRunId),
                    nameof(AgentStepRun.StepIndex),
                    nameof(AgentStepRun.AttemptNumber),
                }));
        Assert.True(unique.IsUnique);

        Assert.Contains(entity.GetIndexes(), i => i.GetFilter()?.Contains("ToolName") == true);
        Assert.Contains(entity.GetIndexes(), i => i.GetFilter()?.Contains("Failed") == true);
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(AgentStepRun.OrganizationId), nameof(AgentStepRun.AgentKey), nameof(AgentStepRun.StartedAt) }));
    }

    [Fact]
    public void StepRun_PersistsStepKindAndStatusAsStrings()
    {
        using var context = CreateContext();
        var entity = DesignTimeModel(context).FindEntityType(typeof(AgentStepRun))!;

        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(AgentStepRun.StepKind))!.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(
            typeof(string),
            entity.FindProperty(nameof(AgentStepRun.Status))!.GetTypeMapping().Converter?.ProviderClrType);
    }

    [Fact]
    public void StepRun_ArgsHashIsAFixedLengthDigest()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(AgentStepRun))!
            .FindProperty(nameof(AgentStepRun.ArgsHash))!;

        Assert.Equal(64, property.GetMaxLength());
    }

    [Fact]
    public void UsageRecord_LinksToOneWorkflowRun()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AiUsageRecord))!;

        var property = entity.FindProperty(nameof(AiUsageRecord.AgentWorkflowRunId));
        Assert.NotNull(property);

        var foreignKey = entity.GetForeignKeys().Single(fk =>
            fk.Properties.Any(p => p.Name == nameof(AiUsageRecord.AgentWorkflowRunId)));
        Assert.Equal(typeof(AgentWorkflowRun), foreignKey.PrincipalEntityType.ClrType);

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(AiUsageRecord.AgentWorkflowRunId));
        Assert.True(index.IsUnique);
        Assert.Contains("AgentWorkflowRunId", index.GetFilter());
    }

    [Fact]
    public void WorkflowRun_CascadesItsSteps()
    {
        using var context = CreateContext();
        var stepEntity = context.Model.FindEntityType(typeof(AgentStepRun))!;

        var foreignKey = stepEntity.GetForeignKeys().Single(fk =>
            fk.Properties.Any(p => p.Name == nameof(AgentStepRun.WorkflowRunId)));

        Assert.Equal(typeof(AgentWorkflowRun), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }
}
