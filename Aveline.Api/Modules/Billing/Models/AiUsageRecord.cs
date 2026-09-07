namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// An immutable, append-only record of the AI usage consumed by a single agent workflow.
/// </summary>
/// <remarks>
/// One row is written per completed workflow invocation.
/// Rows must never be updated or deleted — they form the audit trail for all Blossom
/// consumption and are the source of truth for usage analytics and future cost validation.
///
/// <c>BlossomUnits</c> is always calculated server-side by <c>UsageTrackerService</c>
/// and is never accepted from an external caller.
/// </remarks>
public class AiUsageRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The organisation whose Blossom balance was consumed.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Correlation ID from the originating HTTP request, used to trace a workflow
    /// invocation back through service logs.
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>The LangGraph thread/workflow ID for this invocation.</summary>
    public string WorkflowId { get; set; } = string.Empty;

    /// <summary>The AI provider used (e.g. <c>openai</c>).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The specific model used (e.g. <c>gpt-4o</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Number of input (prompt) tokens consumed across all LLM calls in the workflow.</summary>
    public int InputTokens { get; set; }

    /// <summary>Number of output (completion) tokens generated across all LLM calls in the workflow.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Number of cached tokens applied (reduces cost but still counts toward usage).</summary>
    public int CachedTokens { get; set; }

    /// <summary>
    /// Raw provider cost in USD for this workflow, as reported by the LLM provider's
    /// usage metadata. Stored with 8 decimal places for precision.
    /// </summary>
    public decimal ActualCostUsd { get; set; }

    /// <summary>
    /// Blossom units consumed by this workflow, calculated server-side using the
    /// formula: <c>ceil((InputTokens + OutputTokens + CachedTokens) / 1000, 1 dp)</c>,
    /// minimum 0.1. See ADR-010.
    /// </summary>
    public decimal BlossomUnits { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
