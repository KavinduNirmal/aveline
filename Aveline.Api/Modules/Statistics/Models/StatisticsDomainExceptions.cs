namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>A malformed ingestion payload (BR-5.1–BR-5.3, BR-5.5–BR-5.7) → HTTP 400.</summary>
public class AgentRunValidationException(string message) : Exception(message);

/// <summary>
/// A report that conflicts with an already-terminal run (FR-5.10, FR-5.12) → HTTP 409.
/// Terminal runs are immutable.
/// </summary>
public sealed class AgentRunConflictException(string message) : Exception(message);

/// <summary>The reported run does not exist for the caller's organisation → HTTP 404.</summary>
public sealed class AgentRunNotFoundException(string message) : Exception(message);

/// <summary>
/// The report carries more steps than <c>AgentStats:MaxStepsPerRun</c> (BR-5.8) → HTTP 413.
/// </summary>
public sealed class AgentStepCapExceededException(int limit)
    : Exception($"The run exceeds the maximum of {limit} steps per run.")
{
    public int Limit { get; } = limit;
}
