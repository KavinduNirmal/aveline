using Aveline.Api.Modules.Billing.DTOs;
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

        group.MapGet("/tenant/{organizationId:guid}", GetTenantUsageAsync)
            .WithName("GetTenantUsage")
            .WithSummary(
                "Get one organisation's own account position: its Blossom balance and its standing " +
                "against the plan's staff and customer limits.")
            .Produces<TenantUsageSnapshotDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>
    /// The tenant's own account figures, for Aveline to answer questions about this boutique
    /// (ADR-026).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the first internal endpoint that reports a tenant's *business* position rather than
    /// an agent-internal one, so the audience is decided by the caller: the agent service will only
    /// ask for an organisation whose request carried explicit staff evidence. Nothing here is
    /// scoped by an end-user token, because there is no end user on this call - the agent service
    /// is the only consumer and it authenticates as an internal service.
    /// </para>
    /// <para>
    /// Both numbers are read from the services that already own them, never recomputed: the
    /// entitlement layer for the plan limits, and the balance projection the boutique's own Blossom
    /// meter renders. A figure that disagreed with the dashboard would be a bug in the product, not
    /// a rounding difference.
    /// </para>
    /// </remarks>
    private static async Task<IResult> GetTenantUsageAsync(
        Guid organizationId,
        IBlossomService blossoms,
        ISubscriptionService subscriptions,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            // Entitlement usage is read first, deliberately: it validates that the organisation
            // exists. The balance read creates the period account on demand, so asking it about an
            // unknown organisation would leave a stray account behind.
            var usage = await subscriptions.GetEntitlementUsageAsync(organizationId, cancellationToken);

            // The authoritative balance, not `usage`'s Blossom row: that row compares consumption
            // against the resolved entitlement limit and knows nothing about grants, adjustments or
            // the expiring remainder, so a remainder derived from it would disagree with the meter.
            var balance = await blossoms.GetBalanceAsync(organizationId, cancellationToken);

            var threshold = configuration.GetValue("Billing:LowBalanceThresholdPercent", 20m);

            return Results.Ok(TenantUsageSnapshotDto.From(
                organizationId, balance, usage, threshold, DateTime.UtcNow));
        }
        catch (BlossomOrganizationNotFoundException notFound)
        {
            return Results.NotFound(new { message = notFound.Message });
        }
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
