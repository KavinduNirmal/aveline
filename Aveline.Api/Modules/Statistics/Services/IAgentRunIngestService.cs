using Aveline.Api.Modules.Statistics.DTOs;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Ingestion for the agent service (FR-5.1–FR-5.12). Idempotent on
/// <c>(OrganizationId, WorkflowId)</c>: a re-report updates a non-terminal run and a
/// conflicting report against a terminal run is rejected.
/// </summary>
public interface IAgentRunIngestService
{
    Task<AgentRunIngestResultDto> ReportRunAsync(
        AgentRunReportRequest request, CancellationToken cancellationToken);

    Task<AgentRunIngestResultDto> AppendStepsAsync(
        string workflowId, AgentStepAppendRequest request, CancellationToken cancellationToken);

    /// <summary>Returns the run and its ordered steps, or <c>null</c> when out of scope.</summary>
    Task<AgentRunDetailDto?> GetRunAsync(
        Guid? organizationId, string workflowId, CancellationToken cancellationToken);
}
