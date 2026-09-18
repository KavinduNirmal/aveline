using Aveline.Api.Configurations;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Services;

namespace Aveline.Api.Modules.Statistics.Endpoints;

/// <summary>
/// Internal ingestion for the agent service (docs/api/README.md §C.6, FR-5.1–FR-5.12).
/// Authenticated with the <c>X-Internal-Token</c> header (ADR-009); never exposed to
/// boutique end-users.
/// </summary>
public static class InternalAgentRunEndpoints
{
    public static IEndpointRouteBuilder MapInternalAgentRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/internal/agent-runs")
            .WithTags("Agent Runs (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        group.MapPost("", ReportRunAsync)
            .WithName("ReportAgentRun")
            .WithSummary("Report a complete agent workflow run with its steps (idempotent).")
            .Produces<AgentRunIngestResultDto>(StatusCodes.Status201Created)
            .Produces<AgentRunIngestResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{workflowId}/steps", AppendStepsAsync)
            .WithName("AppendAgentRunSteps")
            .WithSummary("Append steps to a non-terminal agent run.")
            .Produces<AgentRunIngestResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{workflowId}", GetRunAsync)
            .WithName("GetAgentRun")
            .WithSummary("Get one agent run with its ordered steps.")
            .Produces<AgentRunDetailDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> ReportRunAsync(
        AgentRunReportRequest request,
        IAgentRunIngestService ingest,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ingest.ReportRunAsync(request, cancellationToken);
            return result.Created
                ? Results.Created($"/internal/agent-runs/{result.WorkflowId}", result)
                : Results.Ok(result);
        }
        catch (Exception exception)
        {
            return MapProblem(exception);
        }
    }

    private static async Task<IResult> AppendStepsAsync(
        string workflowId,
        AgentStepAppendRequest request,
        IAgentRunIngestService ingest,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await ingest.AppendStepsAsync(workflowId, request, cancellationToken));
        }
        catch (Exception exception)
        {
            return MapProblem(exception);
        }
    }

    private static async Task<IResult> GetRunAsync(
        string workflowId,
        Guid? organizationId,
        IAgentRunIngestService ingest,
        CancellationToken cancellationToken)
    {
        var detail = await ingest.GetRunAsync(organizationId, workflowId, cancellationToken);
        return detail is null
            ? Results.NotFound(new { message = $"Workflow '{workflowId}' was not found." })
            : Results.Ok(detail);
    }

    private static IResult MapProblem(Exception exception) => exception switch
    {
        AgentStepCapExceededException cap => Results.Json(
            new { message = cap.Message, limit = cap.Limit },
            statusCode: StatusCodes.Status413PayloadTooLarge),
        AgentRunConflictException conflict => Results.Conflict(new { message = conflict.Message }),
        AgentRunNotFoundException notFound => Results.NotFound(new { message = notFound.Message }),
        AgentRunValidationException validation => Results.BadRequest(new { message = validation.Message }),
        _ => throw exception,
    };
}
