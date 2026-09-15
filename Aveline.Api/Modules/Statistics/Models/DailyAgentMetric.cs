namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>
/// Pre-computed daily summary rollup for agentic runs (S-13…S-23).
/// Keyed on (OrganizationId, AgentKey, Day) to enable fast historical aggregations.
/// </summary>
public class DailyAgentMetric
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? OrganizationId { get; set; }

    public string AgentKey { get; set; } = "orchestrator";

    public DateTime Day { get; set; }

    public int RunCount { get; set; }

    public int SucceededCount { get; set; }

    public int FailedCount { get; set; }

    public int PausedCount { get; set; }

    public int TimedOutCount { get; set; }

    public int CancelledCount { get; set; }

    public long TotalDurationMs { get; set; }

    public int MaxDurationMs { get; set; }

    public double? AvgDurationMs { get; set; }

    public double? P50DurationMs { get; set; }

    public double? P95DurationMs { get; set; }

    public double? P99DurationMs { get; set; }

    public int StepCount { get; set; }

    public int ToolCallCount { get; set; }

    public int RetryCount { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CachedTokens { get; set; }

    public decimal ActualCostUsd { get; set; }

    public decimal BlossomUnits { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
