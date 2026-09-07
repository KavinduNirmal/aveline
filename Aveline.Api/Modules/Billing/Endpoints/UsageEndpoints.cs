using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// Internal endpoints for recording and querying AI usage data.
/// All routes require the <c>X-Internal-Token</c> header (see ADR-009).
/// These endpoints are consumed by the agent service and administrative tooling only —
/// they are never exposed to boutique end-users.
/// </summary>
public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/usage")
            .WithTags("Usage (Internal)")
            .RequireAuthorization("InternalServicePolicy");

        group.MapPost("/record", RecordUsageAsync)
            .WithName("RecordUsage")
            .WithSummary("Submit AI usage from a completed agent workflow.")
            .Produces<AiUsageRecordResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/summary/{orgId:guid}", GetUsageSummaryAsync)
            .WithName("GetUsageSummary")
            .WithSummary("Get the current billing period summary for an organisation.")
            .Produces<UsageSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/records/{orgId:guid}", GetUsageRecordsAsync)
            .WithName("GetUsageRecords")
            .WithSummary("Get paginated raw usage records for an organisation.")
            .Produces<IReadOnlyList<AiUsageRecordResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> RecordUsageAsync(
        [FromBody] RecordUsageRequest request,
        IUsageTrackerService tracker,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await tracker.RecordWorkflowUsageAsync(request, cancellationToken);
            return Results.Created(
                $"/internal/usage/records/{record.OrganizationId}/{record.Id}",
                AiUsageRecordResponse.From(record));
        }
        catch (InvalidUsageRecordException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetUsageSummaryAsync(
        Guid orgId,
        IUsageTrackerService tracker,
        CancellationToken cancellationToken)
    {
        var summary = await tracker.GetUsageSummaryAsync(orgId, cancellationToken);
        return Results.Ok(summary);
    }

    private static async Task<IResult> GetUsageRecordsAsync(
        Guid orgId,
        IUsageTrackerService tracker,
        IUsageRepository repository,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var records = await repository.GetUsageRecordsAsync(orgId, page, pageSize, cancellationToken);
        return Results.Ok(records.Select(AiUsageRecordResponse.From));
    }
}

/// <summary>Response DTO for a single usage record.</summary>
public sealed record AiUsageRecordResponse(
    Guid Id,
    Guid OrganizationId,
    string WorkflowId,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    decimal ActualCostUsd,
    decimal BlossomUnits,
    DateTime CreatedAt)
{
    public static AiUsageRecordResponse From(AiUsageRecord r) => new(
        r.Id, r.OrganizationId, r.WorkflowId,
        r.Provider, r.Model,
        r.InputTokens, r.OutputTokens, r.CachedTokens,
        r.ActualCostUsd, r.BlossomUnits, r.CreatedAt);
}
