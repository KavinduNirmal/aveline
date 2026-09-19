using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 4b — the <c>dataQuality</c> derivation (M-6). <see cref="AgentDataQualityDto.Derive"/>
/// existed with zero call sites; these tests pin the derivation's truth table (including an
/// empty window where every flag is <c>false</c>) and the two call sites that previously
/// hard-coded the flags.
///
/// The sequencing rule is enforced by construction: a flag is <c>true</c> only when a real row
/// justifies it, and <c>false</c> on an empty window. An instrument existing is never enough.
/// </summary>
public class AgentDataQualityDerivationTests
{
    // ---------------------------------------------------------------------
    // The truth table
    // ---------------------------------------------------------------------

    public static IEnumerable<object[]> DerivationCases()
    {
        // name, latency, nodeFailures, perStep, tools, cost
        yield return ["empty-window", false, false, false, false, false];
        yield return ["latency-present", true, false, false, false, false];
        yield return ["latency-absent", false, false, false, false, false];
        yield return ["failures-present-on-run", false, true, false, false, false];
        yield return ["failures-present-on-step", false, true, false, false, false];
        yield return ["failures-absent", false, false, false, false, false];
        yield return ["per-step-present-tokens", false, false, true, false, false];
        yield return ["per-step-present-step-count", false, false, true, false, false];
        yield return ["per-step-absent", false, false, false, false, false];
        yield return ["tools-present-on-run", false, false, false, true, false];
        yield return ["tools-present-on-step", false, false, false, true, false];
        yield return ["tools-absent", false, false, false, false, false];
        yield return ["cost-present", false, false, false, false, true];
        yield return ["cost-absent", false, false, false, false, false];
    }

    [Theory]
    [MemberData(nameof(DerivationCases))]
    public void Derive_ReturnsTheTruthTable(
        string caseName, bool latency, bool nodeFailures, bool perStep, bool tools, bool cost)
    {
        var (runs, steps) = Fixture(caseName);

        var quality = AgentDataQualityDto.Derive(runs, steps);

        Assert.Equal(
            new AgentDataQualityDto(latency, nodeFailures, perStep, tools, cost),
            quality);
    }

    [Fact]
    public void Derive_EmptyWindow_EveryFlagIsFalse()
    {
        var quality = AgentDataQualityDto.Derive([], []);

        Assert.False(quality.LatencyInstrumented);
        Assert.False(quality.NodeFailuresObserved);
        Assert.False(quality.PerStepAttribution);
        Assert.False(quality.ToolInstrumented);
        Assert.False(quality.CostInstrumented);
    }

    // ---------------------------------------------------------------------
    // The call sites (these fail before the hard-coded flags are replaced)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AgentRunIngestService_GetRun_DerivesDataQualityFromRows()
    {
        await using var context = Context();
        var organizationId = Guid.CreateVersion7();
        var run = Run(organizationId, "wf-derive", durationMs: 250, stepCount: 1);
        context.AgentWorkflowRuns.Add(run);
        context.AgentStepRuns.Add(Step(run.Id, organizationId, 0, inputTokens: 10));
        await context.SaveChangesAsync();

        var detail = await IngestService(context)
            .GetRunAsync(organizationId, "wf-derive", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail!.DataQuality.LatencyInstrumented);
        Assert.True(detail.DataQuality.PerStepAttribution);
        Assert.False(detail.DataQuality.CostInstrumented);
    }

    [Fact]
    public async Task AgentRunIngestService_GetRun_UninstrumentedRun_IsHonestlyFalse()
    {
        await using var context = Context();
        var organizationId = Guid.CreateVersion7();
        context.AgentWorkflowRuns.Add(Run(organizationId, "wf-bare"));
        await context.SaveChangesAsync();

        var detail = await IngestService(context)
            .GetRunAsync(organizationId, "wf-bare", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(AgentDataQualityDto.Uninstrumented, detail!.DataQuality);
    }

    [Fact]
    public async Task BillingStatistics_Profitability_EmptyWindow_EveryFlagIsFalse()
    {
        await using var context = Context();
        var service = BillingService(context);

        var result = await service.GetProfitabilityAsync(null, null, null, CancellationToken.None);

        Assert.Equal(AgentDataQualityDto.Uninstrumented, result.DataQuality);
    }

    [Fact]
    public async Task BillingStatistics_Profitability_DerivesDataQualityFromRows()
    {
        await using var context = Context();
        var organizationId = Guid.CreateVersion7();
        var run = Run(organizationId, "wf-billing", durationMs: 180, costUsd: 0.75m);
        context.AgentWorkflowRuns.Add(run);
        context.AgentStepRuns.Add(Step(run.Id, organizationId, 0, toolName: "search_inventory"));
        await context.SaveChangesAsync();

        var result = await BillingService(context)
            .GetProfitabilityAsync(null, null, null, CancellationToken.None);

        Assert.True(result.DataQuality.LatencyInstrumented);
        Assert.True(result.DataQuality.ToolInstrumented);
        Assert.True(result.DataQuality.CostInstrumented);
        Assert.False(result.DataQuality.NodeFailuresObserved);
    }

    [Fact]
    public async Task BillingStatistics_BurnRate_EmptyWindow_EveryFlagIsFalse()
    {
        await using var context = Context();
        var service = BillingService(context);

        var result = await service.GetBurnRateAsync(Guid.CreateVersion7(), "30d", CancellationToken.None);

        Assert.Equal(AgentDataQualityDto.Uninstrumented, result.DataQuality);
    }

    // ---------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------

    private static (IReadOnlyCollection<AgentWorkflowRun> Runs, IReadOnlyCollection<AgentStepRun> Steps) Fixture(
        string caseName)
    {
        var organizationId = Guid.CreateVersion7();
        var run = Run(organizationId, "wf-case");
        var runs = new List<AgentWorkflowRun> { run };
        var steps = new List<AgentStepRun>();

        switch (caseName)
        {
            case "empty-window":
                return ([], []);
            case "latency-present":
                run.DurationMs = 120;
                break;
            case "latency-absent":
                run.DurationMs = null;
                break;
            case "failures-present-on-run":
                run.Status = AgentRunStatus.Failed;
                break;
            case "failures-present-on-step":
                steps.Add(Step(run.Id, organizationId, 0, status: AgentStepStatus.Failed));
                break;
            case "failures-absent":
                run.Status = AgentRunStatus.Succeeded;
                break;
            case "per-step-present-tokens":
                steps.Add(Step(run.Id, organizationId, 0, inputTokens: 25));
                break;
            case "per-step-present-step-count":
                run.StepCount = 3;
                break;
            case "per-step-absent":
                run.StepCount = 0;
                steps.Clear();
                break;
            case "tools-present-on-run":
                run.ToolCallCount = 2;
                break;
            case "tools-present-on-step":
                steps.Add(Step(run.Id, organizationId, 0, toolName: "lookup_customers"));
                break;
            case "tools-absent":
                run.ToolCallCount = 0;
                steps.Clear();
                break;
            case "cost-present":
                run.ActualCostUsd = 0.42m;
                break;
            case "cost-absent":
                run.ActualCostUsd = 0m;
                break;
        }

        return (runs, steps);
    }

    private static AgentWorkflowRun Run(
        Guid? organizationId,
        string workflowId,
        AgentRunStatus status = AgentRunStatus.Succeeded,
        int? durationMs = null,
        int stepCount = 0,
        int toolCallCount = 0,
        decimal costUsd = 0m,
        DateTime? startedAt = null,
        DateTime? completedAt = null)
    {
        var start = startedAt ?? DateTime.UtcNow.AddMinutes(-5);
        return new AgentWorkflowRun
        {
            OrganizationId = organizationId,
            WorkflowId = workflowId,
            Status = status,
            StartedAt = start,
            CompletedAt = completedAt ?? start.AddSeconds(1),
            DurationMs = durationMs,
            StepCount = stepCount,
            ToolCallCount = toolCallCount,
            ActualCostUsd = costUsd,
            AgentsInvolved = ["orchestrator"],
            IsUnattributed = organizationId is null,
        };
    }

    private static AgentStepRun Step(
        Guid workflowRunId,
        Guid? organizationId,
        short stepIndex,
        AgentStepStatus status = AgentStepStatus.Succeeded,
        string? toolName = null,
        int inputTokens = 0)
    {
        return new AgentStepRun
        {
            WorkflowRunId = workflowRunId,
            OrganizationId = organizationId,
            StepIndex = stepIndex,
            AgentKey = "orchestrator",
            NodeName = $"node_{stepIndex}",
            StepKind = toolName is null ? AgentStepKind.LlmCall : AgentStepKind.ToolCall,
            ToolName = toolName,
            Status = status,
            AttemptNumber = 1,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            InputTokens = inputTokens,
        };
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"agent-data-quality-{Guid.CreateVersion7()}")
            .Options);

    private static AgentRunIngestService IngestService(AppDbContext context)
    {
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<object?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new AgentRunIngestService(
            new AgentRunRepository(context),
            context,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            eventBus.Object,
            NullLogger<AgentRunIngestService>.Instance);
    }

    private static BillingStatisticsService BillingService(AppDbContext context)
    {
        var blossom = new Mock<IBlossomService>();
        blossom
            .Setup(service => service.GetBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid organizationId, CancellationToken _) => new BlossomBalance(
                organizationId,
                DateTime.UtcNow.AddDays(-30),
                DateTime.UtcNow,
                false,
                null,
                0m,
                0m,
                0m,
                0m,
                0m,
                DateTime.UtcNow));

        var entitlements = new Mock<IEntitlementResolver>();

        return new BillingStatisticsService(context, blossom.Object, entitlements.Object);
    }
}
