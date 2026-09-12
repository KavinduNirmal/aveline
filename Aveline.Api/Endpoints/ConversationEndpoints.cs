using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Org-scoped conversation ("The Salon") endpoints. Routed under
/// <c>/orgs/&#123;organizationId&#125;/conversations</c> so the existing
/// <see cref="Aveline.Api.Authorization.OrganizationScopeAuthorizationHandler"/> resolves the
/// target organization from the route and enforces an active membership granting
/// <c>conversations:view</c>.
/// </summary>
public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orgs/{organizationId:guid}/conversations")
            .WithTags("Conversations")
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueConversationAccessPolicy);

        group.MapGet("", ListAsync)
            .WithName("ListConversations")
            .WithSummary("List the organization's conversations (paginated).")
            .Produces<ConversationPage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("", CreateAsync)
            .WithName("CreateConversation")
            .WithSummary("Get-or-create the Salon for an optional customer.")
            .Produces<ConversationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{conversationId:guid}", GetByIdAsync)
            .WithName("GetConversation")
            .WithSummary("Get a single conversation by id.")
            .Produces<ConversationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{conversationId:guid}/messages", ListMessagesAsync)
            .WithName("ListConversationMessages")
            .WithSummary("List a conversation's messages (paginated, optional deep-link).")
            .Produces<MessagePage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{conversationId:guid}/messages", SendMessageAsync)
            .WithName("SendConversationMessage")
            .WithSummary("Send a staff note and trigger the agent.")
            .Produces<MessageDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{conversationId:guid}/select-customer", SelectCustomerAsync)
            .WithName("SelectConversationCustomer")
            .WithSummary("Bind a Salon to a customer chosen from a resolution choice block and re-trigger the agent.")
            .Produces<ConversationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{conversationId:guid}/messages/{messageId:guid}/sign-off", DecideSignOffAsync)
            .WithName("DecideConversationSignOff")
            .WithSummary("Approve or reject a SignOff message.")
            .Produces<MessageDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid organizationId,
        IConversationService conversations,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await conversations.ListAsync(organizationId, page, pageSize, cancellationToken);
        return Results.Ok(new ConversationPage(items, total, page, pageSize));
    }

    private static async Task<IResult> CreateAsync(
        Guid organizationId,
        CreateConversationRequest request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var conversation = await conversations.GetOrCreateSalonAsync(organizationId, userId.Value, request.CustomerId, cancellationToken);
        return Results.Ok(conversation);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid organizationId,
        Guid conversationId,
        IConversationService conversations,
        CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetAsync(organizationId, conversationId, cancellationToken);
        return conversation is null
            ? Results.NotFound(new { message = "Conversation not found." })
            : Results.Ok(conversation);
    }

    private static async Task<IResult> ListMessagesAsync(
        Guid organizationId,
        Guid conversationId,
        IConversationService conversations,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 50,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? around = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await conversations.ListMessagesAsync(organizationId, conversationId, page, pageSize, around, cancellationToken);
        return Results.Ok(new MessagePage(items, total, page, pageSize));
    }

    private static async Task<IResult> SendMessageAsync(
        Guid organizationId,
        Guid conversationId,
        SendMessageRequest request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { message = "Message text is required." });
        }

        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var message = await conversations.SendStaffNoteAsync(organizationId, userId.Value, conversationId, request.Text, cancellationToken);
            return Results.Ok(message);
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { message = "Conversation not found." });
        }
    }

    private static async Task<IResult> DecideSignOffAsync(
        Guid organizationId,
        Guid conversationId,
        Guid messageId,
        SignOffDecisionRequest request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ContentHash))
        {
            return Results.BadRequest(new { message = "A content hash is required to decide a SignOff." });
        }

        try
        {
            var message = await conversations.DecideSignOffAsync(
                organizationId, userId.Value, conversationId, messageId, request.Approved, request.ContentHash, cancellationToken);
            return Results.Ok(message);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> SelectCustomerAsync(
        Guid organizationId,
        Guid conversationId,
        SelectCustomerRequest request,
        IConversationService conversations,
        CancellationToken cancellationToken = default)
    {
        if (request.CustomerId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "A customerId is required." });
        }

        var conversation = await conversations.SelectCustomerAsync(
            organizationId, conversationId, request.CustomerId, request.Query, cancellationToken);

        return conversation is null
            ? Results.NotFound(new { message = "Conversation not found." })
            : Results.Ok(conversation);
    }

    private static async Task<Guid?> ResolveUserIdAsync(
        ClaimsPrincipal user,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return null;
        }

        var dbUser = await users.GetByClerkIdAsync(clerkId, cancellationToken);
        return dbUser?.Id;
    }
}
