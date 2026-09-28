using Aveline.Api.Configurations;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Internal (service-to-service) read access to a conversation's transcript for the agent
/// service (ADR-023, W1.1). Requires the <c>X-Internal-Token</c> header (ADR-009,
/// "InternalServicePolicy") and is never exposed to boutique end-users.
/// </summary>
/// <remarks>
/// This exists because the agent previously received only the newest message and therefore could
/// not resolve anything referential ("yes, that one"). It is scoped per organization rather than
/// per staff user: the agent service is not a user, so the existing staff-facing messages route
/// (which enforces per-user visibility) is not a usable seam for it.
/// </remarks>
public static class InternalConversationEndpoints
{
    /// <summary>How many turns are returned when the caller does not ask for a specific window.</summary>
    private const int DefaultLimit = 20;

    public static IEndpointRouteBuilder MapInternalConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/internal/conversations")
            .WithTags("Conversations (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        group.MapGet("/{conversationId:guid}/messages", GetMessagesAsync)
            .WithName("GetConversationHistory")
            .WithSummary("Get a bounded, oldest-first transcript window for the agent service.")
            .Produces<ConversationHistoryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> GetMessagesAsync(
        Guid conversationId,
        IConversationService conversations,
        [FromQuery] Guid organizationId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "organizationId is required." });
        }

        var history = await conversations.GetHistoryAsync(
            organizationId, conversationId, limit ?? DefaultLimit, cancellationToken);

        return history is null
            ? Results.NotFound(new
            {
                message = $"Conversation '{conversationId}' was not found in organization '{organizationId}'.",
            })
            : Results.Ok(history);
    }
}
