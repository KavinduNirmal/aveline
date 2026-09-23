using Aveline.Api.Configurations;
using Aveline.Api.Modules.Handbook.DTOs;
using Aveline.Api.Modules.Handbook.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Internal (service-to-service) endpoints for the handbook knowledge base (ADR-025).
///
/// <para>
/// All routes require the <c>X-Internal-Token</c> header (ADR-009, "InternalServicePolicy") and
/// are consumed by the Python agent service (search) and the seeder CLI (ingest) only. They are
/// never exposed to boutique end-users, and there is no user-facing write path.
/// </para>
/// </summary>
public static class HandbookEndpoints
{
    public static IEndpointRouteBuilder MapHandbookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/internal/handbook")
            .WithTags("Handbook (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        group.MapPost("/chunks", UpsertChunkAsync)
            .WithName("UpsertHandbookChunk")
            .WithSummary("Insert or update one handbook chunk at (SourceKey, Ordinal), embedding its content.")
            .Produces<HandbookChunkDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/search", SearchAsync)
            .WithName("SearchHandbook")
            .WithSummary("Search the handbook (hybrid by default; lexical or vector for evaluation).")
            .Produces<IReadOnlyList<HandbookSearchResultDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        // A catch-all segment, not `{sourceKey}`: source keys are namespaced ("web-docs/team") and a
        // single path segment cannot carry the slash.
        group.MapDelete("/sources/{**sourceKey}", DeleteSourceAsync)
            .WithName("DeleteHandbookSource")
            .WithSummary("Delete every chunk for a source (used to re-seed or retire a source).")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/sources", ListSourcesAsync)
            .WithName("ListHandbookSources")
            .WithSummary("List indexed sources with chunk counts and the last update time.")
            .Produces<IReadOnlyList<HandbookSourceSummaryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> UpsertChunkAsync(
        SaveHandbookChunkRequest request,
        IHandbookService handbook,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourceKey))
        {
            return Results.BadRequest(new { message = "SourceKey is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { message = "Content is required." });
        }

        if (string.IsNullOrWhiteSpace(request.ContentHash))
        {
            return Results.BadRequest(new { message = "ContentHash is required." });
        }

        var saved = await handbook.UpsertAsync(request, cancellationToken);
        return Results.Ok(saved);
    }

    private static async Task<IResult> SearchAsync(
        HandbookSearchRequest request,
        IHandbookService handbook,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new { message = "Query is required." });
        }

        var results = await handbook.SearchAsync(request, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> DeleteSourceAsync(
        string sourceKey,
        IHandbookService handbook,
        CancellationToken cancellationToken)
    {
        var removed = await handbook.DeleteSourceAsync(sourceKey, cancellationToken);
        return Results.Ok(new { sourceKey, removed });
    }

    private static async Task<IResult> ListSourcesAsync(
        IHandbookService handbook,
        CancellationToken cancellationToken)
        => Results.Ok(await handbook.ListSourcesAsync(cancellationToken));
}
