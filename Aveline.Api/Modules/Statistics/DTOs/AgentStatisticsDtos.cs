using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.DTOs;

/// <summary>
/// Honest flags describing how trustworthy the accompanying numbers are
/// (statistics-catalog.md §8). Until the Python instrumentation for gaps G-1…G-14
/// lands every flag is <c>false</c>, so a client can never mistake a zero for a
/// measurement (FR-5.x residue, risk R-1).
/// </summary>
public sealed record AgentDataQualityDto(
    bool LatencyInstrumented,
    bool NodeFailuresObserved,
    bool PerStepAttribution,
    bool ToolInstrumented,
    bool CostInstrumented)
{
    /// <summary>The current state: nothing is instrumented yet.</summary>
    public static readonly AgentDataQualityDto Uninstrumented = new(
        LatencyInstrumented: false,
        NodeFailuresObserved: false,
        PerStepAttribution: false,
        ToolInstrumented: false,
        CostInstrumented: false);
}

/// <summary>One run as it appears in the paged list (S-13, S-22 summary).</summary>
public sealed record AgentRunSummaryDto(
    Guid Id,
    Guid? OrganizationId,
    string WorkflowId,
    AgentRunStatus Status,
    AgentRunTriggerKind TriggerKind,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    int StepCount,
    int ToolCallCount,
    int RetryCount,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    decimal ActualCostUsd,
    decimal BlossomUnits,
    IReadOnlyList<string> AgentsInvolved,
    string? ErrorCode,
    bool IsUnattributed)
{
    public static AgentRunSummaryDto From(AgentWorkflowRun run) => new(
        run.Id,
        run.OrganizationId,
        run.WorkflowId,
        run.Status,
        run.TriggerKind,
        run.StartedAt,
        run.CompletedAt,
        run.DurationMs,
        run.StepCount,
        run.ToolCallCount,
        run.RetryCount,
        run.InputTokens,
        run.OutputTokens,
        run.CachedTokens,
        run.ActualCostUsd,
        run.BlossomUnits,
        run.AgentsInvolved,
        run.ErrorCode,
        run.IsUnattributed);
}

/// <summary>A page of runs: <c>{ items, page, pageSize, total }</c> plus data quality.</summary>
public sealed record AgentRunPageDto(
    IReadOnlyList<AgentRunSummaryDto> Items,
    int Page,
    int PageSize,
    int Total,
    AgentDataQualityDto DataQuality);

/// <summary>One node execution within a run (S-22).</summary>
public sealed record AgentStepDto(
    Guid Id,
    short StepIndex,
    string AgentKey,
    string NodeName,
    AgentStepKind StepKind,
    string? ToolName,
    AgentStepStatus Status,
    short AttemptNumber,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    string? Provider,
    string? Model,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    decimal ActualCostUsd,
    string? ArgsHash,
    int? ResultBytes,
    string? ErrorCode)
{
    public static AgentStepDto From(AgentStepRun step) => new(
        step.Id,
        step.StepIndex,
        step.AgentKey,
        step.NodeName,
        step.StepKind,
        step.ToolName,
        step.Status,
        step.AttemptNumber,
        step.StartedAt,
        step.CompletedAt,
        step.DurationMs,
        step.Provider,
        step.Model,
        step.InputTokens,
        step.OutputTokens,
        step.CachedTokens,
        step.ActualCostUsd,
        step.ArgsHash,
        step.ResultBytes,
        step.ErrorCode);
}

/// <summary>A run with its ordered step trace (S-22).</summary>
public sealed record AgentRunDetailDto(
    AgentRunSummaryDto Run,
    IReadOnlyList<AgentStepDto> Steps,
    AgentDataQualityDto DataQuality);

/// <summary>Success rate over the terminal share of runs (S-14).</summary>
public sealed record AgentReliabilityDto(
    int TotalRuns,
    int Succeeded,
    int Failed,
    int TimedOut,
    int Cancelled,
    int Running,
    int PausedForApproval,
    int Denominator,
    double? SuccessRate,
    AgentDataQualityDto DataQuality);

/// <summary>One time bucket of the latency series.</summary>
public sealed record AgentLatencyBucketDto(
    DateTime Bucket,
    int SampleSize,
    double? AvgMs,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms);

/// <summary>
/// End-to-end workflow latency (S-16). All fields are <c>null</c> when
/// <c>DurationMs</c> is absent (gap G-3) or below the configured sample floor: an
/// absent measurement must never be rendered as zero.
/// </summary>
public sealed record AgentLatencyDto(
    int SampleSize,
    double? AvgMs,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    IReadOnlyList<AgentLatencyBucketDto> Series,
    AgentDataQualityDto DataQuality);

/// <summary>Per-node latency for one agent (S-17).</summary>
public sealed record AgentStepLatencyItemDto(
    string AgentKey,
    string NodeName,
    int SampleSize,
    double? AvgDurationMs,
    double? P95DurationMs);

public sealed record AgentStepLatencyDto(
    IReadOnlyList<AgentStepLatencyItemDto> Items,
    AgentDataQualityDto DataQuality);

/// <summary>Token usage for one agent/provider/model tuple (S-18).</summary>
public sealed record AgentTokenUsageItemDto(
    string AgentKey,
    string? Provider,
    string? Model,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    int TotalTokens);

public sealed record AgentTokensDto(
    IReadOnlyList<AgentTokenUsageItemDto> Items,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    int TotalTokens,
    AgentDataQualityDto DataQuality);

/// <summary>Actual provider cost (S-19). Always zero until G-5/D-8 is fixed.</summary>
public sealed record AgentCostDto(
    decimal TotalCostUsd,
    double? AvgCostPerRun,
    int RunCount,
    AgentDataQualityDto DataQuality);

/// <summary>Tool call volume, success rate and latency (S-20).</summary>
public sealed record AgentToolUsageItemDto(
    string ToolName,
    int Count,
    double? SuccessRate,
    double? AvgDurationMs);

public sealed record AgentToolsDto(
    IReadOnlyList<AgentToolUsageItemDto> Items,
    AgentDataQualityDto DataQuality);

/// <summary>Failures grouped by cause (S-15).</summary>
public sealed record AgentFailureItemDto(
    string? ErrorCode,
    string? AgentKey,
    string? NodeName,
    string? StepKind,
    int Count);

public sealed record AgentFailuresDto(
    IReadOnlyList<AgentFailureItemDto> Items,
    AgentDataQualityDto DataQuality);

/// <summary>Human-in-the-loop approval wait time (S-21).</summary>
public sealed record AgentApprovalsDto(
    int SampleSize,
    double? AvgWaitMs,
    double? P95WaitMs,
    int PendingCount,
    AgentDataQualityDto DataQuality);

/// <summary>System-wide agent overview for the admin route (S-23 companion).</summary>
public sealed record AgentOverviewDto(
    int TotalRuns,
    int Running,
    int PausedForApproval,
    int Succeeded,
    int Failed,
    double? SuccessRate,
    AgentDataQualityDto DataQuality);
