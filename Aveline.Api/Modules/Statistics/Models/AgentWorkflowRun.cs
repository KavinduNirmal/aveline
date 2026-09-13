using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>
/// One agent workflow invocation — the analytics fact table for agentic statistics
/// (FR-5.1). Unique on <c>(OrganizationId, WorkflowId)</c> so ingestion is idempotent.
/// </summary>
public class AgentWorkflowRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary><c>NULL</c> when no organization could be resolved (D-7).</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>LangGraph thread/run id; unique per organization.</summary>
    public string WorkflowId { get; set; } = string.Empty;

    /// <summary>Set for sub-graph runs.</summary>
    public Guid? ParentWorkflowRunId { get; set; }

    /// <summary>Correlation id from the originating HTTP request.</summary>
    public string? RequestId { get; set; }

    public Guid? TraceId { get; set; }

    public AgentRunTriggerKind TriggerKind { get; set; } = AgentRunTriggerKind.ApiRequest;

    /// <summary>Message id, request id, etc.</summary>
    public string? TriggerRef { get; set; }

    public Guid? ConversationId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? InitiatedByUserId { get; set; }

    public AgentRunStatus Status { get; set; } = AgentRunStatus.Running;

    /// <summary>Distinct agent keys observed in the run.</summary>
    public List<string> AgentsInvolved { get; set; } = [];

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int? DurationMs { get; set; }

    public DateTime? PausedAt { get; set; }

    public DateTime? ResumedAt { get; set; }

    public int? ApprovalWaitMs { get; set; }

    public int StepCount { get; set; }

    public int ToolCallCount { get; set; }

    public int RetryCount { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int CachedTokens { get; set; }

    public long? NormalizedUnits { get; set; }

    public decimal ActualCostUsd { get; set; }

    public decimal BlossomUnits { get; set; }

    public Guid? PricingRuleId { get; set; }

    public string? PlanTierAtRun { get; set; }

    /// <summary>True when <see cref="OrganizationId"/> is <c>NULL</c>.</summary>
    public bool IsUnattributed { get; set; } = true;

    public string? ErrorCode { get; set; }

    /// <summary>Truncated; never contains prompt content (FR-5.9).</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AgentStepRun> Steps { get; set; } = [];
}

/// <summary>
/// One node execution (or tool call) within a workflow (FR-5.2, FR-5.3).
/// </summary>
public class AgentStepRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid WorkflowRunId { get; set; }

    /// <summary>Denormalised so per-organization statistics never join the run table.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>0-based order within the run.</summary>
    public short StepIndex { get; set; }

    /// <summary>Registered agent key: <c>customer_memory</c>, <c>visual_insight</c>, <c>commerce</c>, <c>orchestrator</c>.</summary>
    public string AgentKey { get; set; } = string.Empty;

    public string NodeName { get; set; } = string.Empty;

    public AgentStepKind StepKind { get; set; } = AgentStepKind.LlmCall;

    /// <summary>Set when <see cref="StepKind"/> is <see cref="AgentStepKind.ToolCall"/>.</summary>
    public string? ToolName { get; set; }

    public AgentStepStatus Status { get; set; } = AgentStepStatus.Succeeded;

    public short AttemptNumber { get; set; } = 1;

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int? DurationMs { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int CachedTokens { get; set; }

    public decimal ActualCostUsd { get; set; }

    /// <summary>SHA-256 of canonicalised args. Args are never stored (FR-5.9).</summary>
    public string? ArgsHash { get; set; }

    /// <summary>Result size only; result content is never stored (FR-5.9).</summary>
    public int? ResultBytes { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentRunStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    PausedForApproval,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentRunTriggerKind
{
    WhatsAppInbound,
    WhatsAppOutbound,
    ApiRequest,
    Scheduled,
    Manual,
    Webhook,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentStepKind
{
    LlmCall,
    ToolCall,
    Decision,
    HumanInterrupt,
    Retrieval,
    Validation,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentStepStatus
{
    Succeeded,
    Failed,
    Skipped,
    TimedOut,
}
