using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #210 — the agent statistics query services over the M6 schema. Every read is
/// organisation-scoped through the repository, percentiles respect the sample floor and
/// the <c>dataQuality</c> flags stay honest until the Python instrumentation lands.
/// </summary>
public class AgentStatisticsQueryTests
{
    private static AppDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .Options);

    private static IConfiguration Config(int minSample) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentStats:MinSampleForPercentile"] = minSample.ToString(),
            })
            .Build();

    private static AgentStatisticsService Service(AppDbContext context, int minSample = 20) =>
        new(new AgentRunRepository(context), Config(minSample));

    private static AgentStatisticsQuery Query(Guid? organizationId, int minSampleUnused = 20) =>
        new(organizationId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(1));

    private static AgentWorkflowRun Run(
        Guid? organizationId,
        string workflowId,
        AgentRunStatus status = AgentRunStatus.Succeeded,
        DateTime? startedAt = null,
        int? durationMs = 100,
        int stepCount = 0,
        int inputTokens = 0,
        int outputTokens = 0,
        int cachedTokens = 0,
        decimal cost = 0m,
        int? approvalWaitMs = null,
        string? errorCode = null,
        params string[] agents)
    {
        var start = startedAt ?? DateTime.UtcNow.AddMinutes(-5);
        return new AgentWorkflowRun
        {
            OrganizationId = organizationId,
            WorkflowId = workflowId,
            Status = status,
            StartedAt = start,
            CompletedAt = status is AgentRunStatus.Running or AgentRunStatus.PausedForApproval
                ? null
                : start.AddMilliseconds(durationMs ?? 100),
            DurationMs = durationMs,
            PausedAt = status == AgentRunStatus.PausedForApproval ? start : null,
            ApprovalWaitMs = approvalWaitMs,
            StepCount = stepCount,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CachedTokens = cachedTokens,
            ActualCostUsd = cost,
            ErrorCode = errorCode,
            AgentsInvolved = agents.Length == 0 ? ["orchestrator"] : [.. agents],
            IsUnattributed = organizationId is null,
        };
    }

    private static AgentStepRun Step(
        Guid runId,
        Guid? organizationId,
        short index,
        string agentKey,
        string nodeName,
        AgentStepKind kind = AgentStepKind.LlmCall,
        AgentStepStatus status = AgentStepStatus.Succeeded,
        string? toolName = null,
        int? durationMs = 50,
        int inputTokens = 10,
        int outputTokens = 5,
        string? provider = "openai",
        string? model = "gpt-4o",
        string? errorCode = null) => new()
    {
        WorkflowRunId = runId,
        OrganizationId = organizationId,
        StepIndex = index,
        AgentKey = agentKey,
        NodeName = nodeName,
        StepKind = kind,
        ToolName = toolName,
        Status = status,
        StartedAt = DateTime.UtcNow.AddMinutes(-5),
        CompletedAt = DateTime.UtcNow.AddMinutes(-5).AddMilliseconds(durationMs ?? 0),
        DurationMs = durationMs,
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
        Provider = provider,
        Model = model,
        ErrorCode = errorCode,
    };

    [Fact]
    public async Task GetRuns_IsScopedToTheRequestedOrganization()
    {
        await using var context = CreateContext(nameof(GetRuns_IsScopedToTheRequestedOrganization));
        var orgA = Guid.CreateVersion7();
        var orgB = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(orgA, "wf-a1"), Run(orgA, "wf-a2"), Run(orgB, "wf-b1"), Run(null, "wf-unattributed"));
        await context.SaveChangesAsync();

        var page = await Service(context).GetRunsAsync(Query(orgA), CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, item => Assert.Equal(orgA, item.OrganizationId));
        Assert.DoesNotContain(page.Items, item => item.WorkflowId == "wf-b1");
    }

    [Fact]
    public async Task GetRuns_PagesAndReportsTheTotal()
    {
        await using var context = CreateContext(nameof(GetRuns_PagesAndReportsTheTotal));
        var org = Guid.CreateVersion7();
        for (var i = 0; i < 5; i++)
        {
            context.AgentWorkflowRuns.Add(Run(org, $"wf-{i}", startedAt: DateTime.UtcNow.AddMinutes(-i)));
        }
        await context.SaveChangesAsync();

        var page = await Service(context).GetRunsAsync(
            Query(org) with { Page = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task GetRuns_FiltersByStatusTriggerKindAndAgentKey()
    {
        await using var context = CreateContext(nameof(GetRuns_FiltersByStatusTriggerKindAndAgentKey));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(org, "wf-ok", status: AgentRunStatus.Succeeded, agents: "customer_memory"),
            Run(org, "wf-failed", status: AgentRunStatus.Failed, agents: "visual_insight"),
            Run(org, "wf-other", status: AgentRunStatus.Succeeded, agents: "commerce"));
        await context.SaveChangesAsync();

        var service = Service(context);
        var failed = await service.GetRunsAsync(
            Query(org) with { Status = AgentRunStatus.Failed }, CancellationToken.None);
        Assert.Single(failed.Items);
        Assert.Equal("wf-failed", failed.Items[0].WorkflowId);

        var memory = await service.GetRunsAsync(
            Query(org) with { AgentKey = "customer_memory" }, CancellationToken.None);
        Assert.Single(memory.Items);
        Assert.Equal("wf-ok", memory.Items[0].WorkflowId);
    }

    [Fact]
    public async Task GetReliability_ExcludesRunningAndPausedFromTheDenominator()
    {
        await using var context = CreateContext(nameof(GetReliability_ExcludesRunningAndPausedFromTheDenominator));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(org, "wf-s1", status: AgentRunStatus.Succeeded),
            Run(org, "wf-s2", status: AgentRunStatus.Succeeded),
            Run(org, "wf-f1", status: AgentRunStatus.Failed, errorCode: "agent_error"),
            Run(org, "wf-r1", status: AgentRunStatus.Running),
            Run(org, "wf-p1", status: AgentRunStatus.PausedForApproval));
        await context.SaveChangesAsync();

        var reliability = await Service(context).GetReliabilityAsync(Query(org), CancellationToken.None);

        Assert.Equal(5, reliability.TotalRuns);
        Assert.Equal(3, reliability.Denominator);
        Assert.Equal(2, reliability.Succeeded);
        Assert.Equal(2, reliability.Running + reliability.PausedForApproval);
        Assert.Equal(2d / 3d, reliability.SuccessRate!.Value, 6);
    }

    [Fact]
    public async Task GetLatency_IsNullWhenDurationsAreAbsent()
    {
        await using var context = CreateContext(nameof(GetLatency_IsNullWhenDurationsAreAbsent));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(org, "wf-1", durationMs: null), Run(org, "wf-2", durationMs: null));
        await context.SaveChangesAsync();

        var latency = await Service(context, minSample: 1).GetLatencyAsync(Query(org), CancellationToken.None);

        Assert.Equal(0, latency.SampleSize);
        Assert.Null(latency.AvgMs);
        Assert.Null(latency.P50Ms);
        Assert.Null(latency.P95Ms);
        Assert.Null(latency.P99Ms);
        Assert.Empty(latency.Series);
        Assert.False(latency.DataQuality.LatencyInstrumented);
    }

    [Fact]
    public async Task GetLatency_IsNullBelowTheSampleFloor()
    {
        await using var context = CreateContext(nameof(GetLatency_IsNullBelowTheSampleFloor));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(org, "wf-1", durationMs: 100), Run(org, "wf-2", durationMs: 200));
        await context.SaveChangesAsync();

        var latency = await Service(context, minSample: 3).GetLatencyAsync(Query(org), CancellationToken.None);

        Assert.Equal(2, latency.SampleSize);
        Assert.Null(latency.AvgMs);
        Assert.Null(latency.P95Ms);
    }

    [Fact]
    public async Task GetLatency_ComputesOnceTheFloorIsMet()
    {
        await using var context = CreateContext(nameof(GetLatency_ComputesOnceTheFloorIsMet));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(org, "wf-1", durationMs: 100),
            Run(org, "wf-2", durationMs: 200),
            Run(org, "wf-3", durationMs: 300));
        await context.SaveChangesAsync();

        var latency = await Service(context, minSample: 3).GetLatencyAsync(Query(org), CancellationToken.None);

        Assert.Equal(3, latency.SampleSize);
        Assert.Equal(200d, latency.AvgMs);
        Assert.Equal(200d, latency.P50Ms);
        Assert.Equal(290d, latency.P95Ms);
    }

    [Fact]
    public async Task GetRunDetail_ReturnsStepsOrderedByIndexAndAttempt()
    {
        await using var context = CreateContext(nameof(GetRunDetail_ReturnsStepsOrderedByIndexAndAttempt));
        var org = Guid.CreateVersion7();
        var run = Run(org, "wf-detail", stepCount: 3);
        context.AgentWorkflowRuns.Add(run);
        context.AgentStepRuns.AddRange(
            Step(run.Id, org, 1, "customer_memory", "recall"),
            Step(run.Id, org, 0, "orchestrator", "plan"),
            Step(run.Id, org, 2, "visual_insight", "analyse"));
        await context.SaveChangesAsync();

        var detail = await Service(context).GetRunDetailAsync(org, run.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(new short[] { 0, 1, 2 }, detail!.Steps.Select(s => s.StepIndex));
    }

    [Fact]
    public async Task GetRunDetail_ForAnotherOrganization_ReturnsNull()
    {
        await using var context = CreateContext(nameof(GetRunDetail_ForAnotherOrganization_ReturnsNull));
        var org = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var run = Run(org, "wf-private");
        context.AgentWorkflowRuns.Add(run);
        await context.SaveChangesAsync();

        Assert.Null(await Service(context).GetRunDetailAsync(other, run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetSteps_GroupsByAgentKeyAndNodeName()
    {
        await using var context = CreateContext(nameof(GetSteps_GroupsByAgentKeyAndNodeName));
        var org = Guid.CreateVersion7();
        var run = Run(org, "wf-steps");
        context.AgentWorkflowRuns.Add(run);
        context.AgentStepRuns.AddRange(
            Step(run.Id, org, 0, "customer_memory", "recall", durationMs: 100),
            Step(run.Id, org, 1, "customer_memory", "recall", durationMs: 300),
            Step(run.Id, org, 2, "visual_insight", "analyse", durationMs: 200));
        await context.SaveChangesAsync();

        var steps = await Service(context, minSample: 2).GetStepsAsync(Query(org), CancellationToken.None);

        var recall = steps.Items.Single(i => i.NodeName == "recall");
        Assert.Equal(2, recall.SampleSize);
        Assert.Equal(200d, recall.AvgDurationMs);
        Assert.Equal(290d, recall.P95DurationMs);

        var analyse = steps.Items.Single(i => i.NodeName == "analyse");
        Assert.Equal(1, analyse.SampleSize);
        Assert.Null(analyse.P95DurationMs);
    }

    [Fact]
    public async Task GetTokens_GroupsByAgentProviderAndModel()
    {
        await using var context = CreateContext(nameof(GetTokens_GroupsByAgentProviderAndModel));
        var org = Guid.CreateVersion7();
        var run = Run(org, "wf-tokens", inputTokens: 30, outputTokens: 15, cachedTokens: 5);
        context.AgentWorkflowRuns.Add(run);
        context.AgentStepRuns.AddRange(
            Step(run.Id, org, 0, "customer_memory", "recall", inputTokens: 20, outputTokens: 10),
            Step(run.Id, org, 1, "customer_memory", "recall", inputTokens: 10, outputTokens: 5),
            Step(run.Id, org, 2, "visual_insight", "analyse", provider: "anthropic", model: "claude"));
        await context.SaveChangesAsync();

        var tokens = await Service(context).GetTokensAsync(Query(org), CancellationToken.None);

        var memory = tokens.Items.Single(i => i.AgentKey == "customer_memory");
        Assert.Equal(30, memory.InputTokens);
        Assert.Equal(15, memory.OutputTokens);
        Assert.Equal(45, memory.TotalTokens);

        Assert.Contains(tokens.Items, i => i.AgentKey == "visual_insight" && i.Provider == "anthropic");
        Assert.Equal(60, tokens.TotalTokens);
        Assert.False(tokens.DataQuality.PerStepAttribution);
    }

    [Fact]
    public async Task GetCost_ComputesTotalAndAveragePerRun()
    {
        await using var context = CreateContext(nameof(GetCost_ComputesTotalAndAveragePerRun));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(org, "wf-c1", cost: 0.25m), Run(org, "wf-c2", cost: 0.75m));
        await context.SaveChangesAsync();

        var cost = await Service(context).GetCostAsync(Query(org), CancellationToken.None);

        Assert.Equal(1.0m, cost.TotalCostUsd);
        Assert.Equal(0.5, cost.AvgCostPerRun);
        Assert.Equal(2, cost.RunCount);
        Assert.False(cost.DataQuality.CostInstrumented);
    }

    [Fact]
    public async Task GetTools_GroupsByToolNameWithSuccessRateAndDuration()
    {
        await using var context = CreateContext(nameof(GetTools_GroupsByToolNameWithSuccessRateAndDuration));
        var org = Guid.CreateVersion7();
        var run = Run(org, "wf-tools");
        context.AgentWorkflowRuns.Add(run);
        context.AgentStepRuns.AddRange(
            Step(run.Id, org, 0, "commerce", "order", AgentStepKind.ToolCall, AgentStepStatus.Succeeded, "create_order", 100),
            Step(run.Id, org, 1, "commerce", "order", AgentStepKind.ToolCall, AgentStepStatus.Failed, "create_order", 300),
            Step(run.Id, org, 2, "commerce", "lookup", AgentStepKind.ToolCall, AgentStepStatus.Succeeded, "list_products", 50));
        await context.SaveChangesAsync();

        var tools = await Service(context).GetToolsAsync(Query(org), CancellationToken.None);

        var order = tools.Items.Single(i => i.ToolName == "create_order");
        Assert.Equal(2, order.Count);
        Assert.Equal(0.5, order.SuccessRate);
        Assert.Equal(200d, order.AvgDurationMs);
        Assert.False(tools.DataQuality.ToolInstrumented);
    }

    [Fact]
    public async Task GetFailures_GroupsByErrorCodeAgentAndNode()
    {
        await using var context = CreateContext(nameof(GetFailures_GroupsByErrorCodeAgentAndNode));
        var org = Guid.CreateVersion7();
        var run = Run(org, "wf-failures", status: AgentRunStatus.Failed, errorCode: "agent_error");
        context.AgentWorkflowRuns.Add(run);
        context.AgentStepRuns.AddRange(
            Step(run.Id, org, 0, "customer_memory", "recall", status: AgentStepStatus.Failed, errorCode: "tool_error"),
            Step(run.Id, org, 1, "customer_memory", "recall", status: AgentStepStatus.Failed, errorCode: "tool_error"),
            Step(run.Id, org, 2, "visual_insight", "analyse", status: AgentStepStatus.Failed, errorCode: "llm_error"));
        await context.SaveChangesAsync();

        var failures = await Service(context).GetFailuresAsync(Query(org), CancellationToken.None);

        Assert.Contains(failures.Items, i => i.ErrorCode == "tool_error" && i.Count == 2 && i.NodeName == "recall");
        Assert.Contains(failures.Items, i => i.ErrorCode == "agent_error" && i.Count == 1);
        Assert.False(failures.DataQuality.NodeFailuresObserved);
    }

    [Fact]
    public async Task GetApprovals_ComputesWaitTimesAndPendingCount()
    {
        await using var context = CreateContext(nameof(GetApprovals_ComputesWaitTimesAndPendingCount));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.AddRange(
            Run(org, "wf-a1", approvalWaitMs: 100),
            Run(org, "wf-a2", approvalWaitMs: 300),
            Run(org, "wf-pending", status: AgentRunStatus.PausedForApproval));
        await context.SaveChangesAsync();

        var approvals = await Service(context, minSample: 2).GetApprovalsAsync(Query(org), CancellationToken.None);

        Assert.Equal(2, approvals.SampleSize);
        Assert.Equal(200d, approvals.AvgWaitMs);
        Assert.Equal(290d, approvals.P95WaitMs);
        Assert.Equal(1, approvals.PendingCount);
    }

    [Fact]
    public async Task EveryResponse_CarriesHonestFalseDataQualityFlags()
    {
        await using var context = CreateContext(nameof(EveryResponse_CarriesHonestFalseDataQualityFlags));
        var org = Guid.CreateVersion7();
        context.AgentWorkflowRuns.Add(Run(org, "wf-dq"));
        await context.SaveChangesAsync();
        var service = Service(context);
        var query = Query(org);

        var flags = new[]
        {
            (await service.GetRunsAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetReliabilityAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetLatencyAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetStepsAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetTokensAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetCostAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetToolsAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetFailuresAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetApprovalsAsync(query, CancellationToken.None)).DataQuality,
            (await service.GetOverviewAsync(query, CancellationToken.None)).DataQuality,
        };

        Assert.All(flags, f =>
        {
            Assert.False(f.LatencyInstrumented);
            Assert.False(f.NodeFailuresObserved);
            Assert.False(f.PerStepAttribution);
            Assert.False(f.ToolInstrumented);
            Assert.False(f.CostInstrumented);
        });
    }
}
