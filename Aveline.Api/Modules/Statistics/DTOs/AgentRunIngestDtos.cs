using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.DTOs;

/// <summary>
/// A batched run report from the agent service (FR-5.1). Deliberately contains no
/// prompt text, tool arguments or tool results — only <c>ArgsHash</c>/<c>ResultBytes</c>
/// on each step (FR-5.9). <see cref="AgentRunPrivacyTests"/> enforces this.
/// </summary>
public sealed record AgentRunReportRequest(
    Guid? OrganizationId,
    string WorkflowId,
    Guid? ParentWorkflowRunId,
    string? RequestId,
    Guid? TraceId,
    AgentRunTriggerKind TriggerKind,
    string? TriggerRef,
    Guid? ConversationId,
    Guid? CustomerId,
    Guid? InitiatedByUserId,
    AgentRunStatus Status,
    string[]? AgentsInvolved,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    DateTime? PausedAt,
    DateTime? ResumedAt,
    int? ApprovalWaitMs,
    int ToolCallCount,
    int RetryCount,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    decimal ActualCostUsd,
    decimal BlossomUnits,
    Guid? PricingRuleId,
    string? PlanTierAtRun,
    string? ErrorCode,
    Guid? AiUsageRecordId,
    IReadOnlyList<AgentStepReportRequest>? Steps = null);

/// <summary>
/// One node execution or tool call within a report (FR-5.2, FR-5.3). Tool data is
/// limited to <see cref="ArgsHash"/> and <see cref="ResultBytes"/> (FR-5.9).
/// </summary>
public sealed record AgentStepReportRequest(
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
    string? ErrorCode);

/// <summary>An incremental step append for a long-running or streaming run (FR-5.2).</summary>
public sealed record AgentStepAppendRequest(
    Guid? OrganizationId,
    IReadOnlyList<AgentStepReportRequest> Steps);

/// <summary>The outcome of an idempotent ingest call.</summary>
public sealed record AgentRunIngestResultDto(
    Guid Id,
    Guid? OrganizationId,
    string WorkflowId,
    AgentRunStatus Status,
    int StepCount,
    bool Created)
{
    public static AgentRunIngestResultDto From(AgentWorkflowRun run, bool created) => new(
        run.Id, run.OrganizationId, run.WorkflowId, run.Status, run.StepCount, created);
}
