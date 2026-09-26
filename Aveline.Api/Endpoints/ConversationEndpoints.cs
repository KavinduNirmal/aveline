using System.Security.Claims;
using Aveline.Api.Common.Media;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Media;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
            .WithSummary("Send a staff note and trigger the agent (idempotent on clientMessageId).")
            .Produces<MessageDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{conversationId:guid}/select-customer", SelectCustomerAsync)
            .WithName("SelectConversationCustomer")
            .WithSummary("Bind a Salon to a customer chosen from a resolution choice block and re-trigger the agent.")
            .Produces<ConversationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        // Deliberately NOT the messages route: sending a note writes into the Salon and reaches no
        // customer, whereas delivering to a customer leaves over their channel. Two promises, two
        // endpoints — see `ICustomerDeliveryService`.
        group.MapPost("/{conversationId:guid}/deliver", DeliverToCustomerAsync)
            .WithName("DeliverConversationBlockToCustomer")
            .WithSummary("Send a block's words to the thread's customer on their own channel.")
            .Produces<DeliveryResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{conversationId:guid}/messages/{messageId:guid}/regenerate", RegenerateAsync)
            .WithName("RegenerateConversationMessage")
            .WithSummary("Re-run the agent for the turn behind a message; the reply arrives on the hub.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        // The release gate is narrowed: the group's `conversations:view` is not enough to
        // release an above-limit commitment, so the approval policy is required on top of it.
        group.MapPost("/{conversationId:guid}/messages/{messageId:guid}/sign-off", DecideSignOffAsync)
            .WithName("DecideConversationSignOff")
            .WithSummary("Approve or reject a SignOff message.")
            .Produces<MessageDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueConversationApprovalPolicy);

        group.MapPost("/{conversationId:guid}/messages/{messageId:guid}/sign-off/revoke", RevokeSignOffAsync)
            .WithName("RevokeConversationSignOff")
            .WithSummary("Revoke an approved SignOff and return it to the associate's queue.")
            .Produces<MessageDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueConversationApprovalPolicy);

        group.MapPost("/{conversationId:guid}/attachments", UploadAttachmentAsync)
            .WithName("UploadConversationAttachment")
            .WithSummary("Upload a thread attachment (image or PDF) and store it unbound.")
            .Produces<AttachmentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        group.MapGet("/{conversationId:guid}/attachments/{attachmentId:guid}", GetAttachmentAsync)
            .WithName("GetConversationAttachment")
            .WithSummary("Serve an attachment's bytes, tenant- and visibility-checked.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{conversationId:guid}/read", MarkReadAsync)
            .WithName("MarkConversationRead")
            .WithSummary("Advance the caller's own read marker for a conversation (monotonic).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid organizationId,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var (items, total) = await conversations.ListAsync(organizationId, userId.Value, page, pageSize, cancellationToken);
        return Results.Ok(new ConversationPage(items, total, page, pageSize));
    }

    private static async Task<IResult> CreateAsync(
        Guid organizationId,
        CreateConversationRequest request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IMessageBroadcaster broadcaster,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var conversation = await conversations.GetOrCreateSalonAsync(organizationId, userId.Value, request.CustomerId, cancellationToken);
        // A brand-new thread has no event path of its own, so the API broadcasts its tile here
        // rather than routing through the unpublished `conversation.created` event.
        await BroadcastTileAsync(conversations, broadcaster, conversation.Id, cancellationToken);
        return Results.Ok(conversation);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid organizationId,
        Guid conversationId,
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

        var conversation = await conversations.GetAsync(organizationId, userId.Value, conversationId, cancellationToken);
        return conversation is null
            ? Results.NotFound(new { message = "Conversation not found." })
            : Results.Ok(conversation);
    }

    private static async Task<IResult> ListMessagesAsync(
        Guid organizationId,
        Guid conversationId,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 50,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? around = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var (items, total, effectivePage) = await conversations.ListMessagesAsync(
                organizationId, userId.Value, conversationId, page, pageSize, around, cancellationToken);
            // The served page is echoed, not the requested one: with `around` the anchor decides
            // the page, and a client that received its own request back could not tell where in
            // the thread it now stands.
            return Results.Ok(new MessagePage(items, total, effectivePage, pageSize));
        }
        catch (InvalidOperationException)
        {
            // The conversation is not in this org or is not visible to the caller. The route
            // advertises a 404 and the send already catches; this read used to reach the global
            // handler and answer 500.
            return Results.NotFound(new { message = "Conversation not found." });
        }
    }

    private static async Task<IResult> SendMessageAsync(
        Guid organizationId,
        Guid conversationId,
        SendMessageRequest request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IMessageBroadcaster broadcaster,
        IUserRepository users,
        IConversationRepository conversationRepository,
        IAttachmentStore attachmentStore,
        IImageUrlFetcher imageUrlFetcher,
        ILoggerFactory loggerFactory,
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

        // The pasted URL, when there is one, is fetched and stored before the message is written:
        // a refused URL adds no attachment (salon plan §7.5 item 14), and the one refusal that is
        // not "send without the image" — the disabled feature — is answered before any write.
        Guid? imageAttachmentId;
        try
        {
            imageAttachmentId = await ResolvePastedImageAsync(
                organizationId, conversationId, userId.Value, request.ImageUrl,
                conversationRepository, attachmentStore, imageUrlFetcher, loggerFactory,
                cancellationToken);
        }
        catch (ImageUrlFetchException refusal) when (refusal.Reason == ImageUrlFetchReasons.Disabled)
        {
            // A deployment that does not offer the feature says so, rather than dropping the image
            // from a message the client thought carried it (strategy §4 C7). The fetcher already
            // logged the refusal at Warning.
            return Results.BadRequest(new
            {
                code = refusal.Reason,
                message = "Pasting an image URL is not enabled on this deployment. Upload the image file instead.",
            });
        }

        try
        {
            var attachmentIds = imageAttachmentId is null
                ? request.AttachmentIds
                : (request.AttachmentIds ?? []).Append(imageAttachmentId.Value).ToList();

            var message = await conversations.SendStaffNoteAsync(
                organizationId, userId.Value, conversationId, request.Text,
                request.ClientMessageId, attachmentIds, cancellationToken);
            // The staff note moves the thread's preview, so the list is told directly rather
            // than waiting for the agent's reply.
            await BroadcastTileAsync(conversations, broadcaster, conversationId, cancellationToken);
            return Results.Ok(message);
        }
        catch (AttachmentBindingException ex)
        {
            // The whole send is refused rather than half-bound, so the uploads stay unbound and
            // the sweep can collect them.
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (MessageIdempotencyConflictException)
        {
            // The key promises exactly one message, so a reuse for different words is refused
            // rather than written as a second row.
            return Results.Conflict(new
            {
                code = "message-idempotency-conflict",
                message = "This message id was already used for different content.",
            });
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { message = "Conversation not found." });
        }
    }

    /// <summary>
    /// Fetches a pasted image URL through the SSRF-guarded fetcher and stores it unbound, or
    /// returns <c>null</c> when there is nothing to fetch or the fetch was refused for any reason
    /// other than the feature being disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal discipline is the webhook's (<c>WebhookEndpoints.cs:346-358</c>; salon plan
    /// §7.5 item 14): a rejected or failed fetch logs at <see cref="LogLevel.Warning"/> and returns
    /// without an attachment, leaving the message's own words to be sent untouched. Only
    /// <see cref="ImageUrlFetchReasons.Disabled"/> escapes, because a deployment that does not
    /// offer the feature can tell the client so — the client can act on that answer by uploading
    /// the file, whereas a refused URL would silently drop the image (strategy §4 C7).
    /// </para>
    /// <para>
    /// The content type stored is the fetcher's <em>sniffed</em> type, never the peer's declared
    /// header: the bytes are the truth (strategy §3.6, salon plan §7.5 item 11). The byte hash is
    /// computed with <see cref="AttachmentContentHash.Compute"/>, the one convention every
    /// conversation attachment shares.
    /// </para>
    /// </remarks>
    private static async Task<Guid?> ResolvePastedImageAsync(
        Guid organizationId,
        Guid conversationId,
        Guid userId,
        string? imageUrl,
        IConversationRepository conversationRepository,
        IAttachmentStore attachmentStore,
        IImageUrlFetcher imageUrlFetcher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var logger = loggerFactory.CreateLogger(typeof(ConversationEndpoints));

        try
        {
            var fetched = await imageUrlFetcher.FetchAsync(imageUrl, cancellationToken);

            // The store's tag and `context` pair need the customer the media concerns, which is
            // the conversation's own binding. An invisible conversation resolves to null here and
            // the send below answers 404, exactly as it would without an image.
            var conversation = await conversationRepository.GetVisibleToUserAsync(
                organizationId, conversationId, userId, cancellationToken);

            var stored = await attachmentStore.StoreAsync(
                new AttachmentStoreRequest(
                    organizationId,
                    conversationId,
                    userId,
                    fetched.Bytes,
                    fetched.ContentType,
                    FileNameFor(fetched.ContentType),
                    Width: null,
                    Height: null,
                    // The provenance is the call path, and this one fetched from a URL.
                    MediaSource.Url,
                    conversation?.CustomerId,
                    AttachmentContentHash.Compute(fetched.Bytes)),
                cancellationToken);

            return stored.Id;
        }
        catch (ImageUrlFetchException refusal) when (refusal.Reason != ImageUrlFetchReasons.Disabled)
        {
            // The message text is untouched and no attachment is added; the send proceeds. The
            // reason travels, never the URL (item 13). The disabled feature is the one refusal
            // this helper does not absorb: the caller answers it with an explicit 400.
            logger.LogWarning(
                "A pasted image URL was refused; the message is sent without it. reason={Reason}",
                refusal.Reason);

            return null;
        }
    }

    /// <summary>A provider-neutral file name, since a fetched URL carries none.</summary>
    private static string FileNameFor(string contentType) => contentType switch
    {
        "image/png" => "pasted-image.png",
        "image/gif" => "pasted-image.gif",
        "image/webp" => "pasted-image.webp",
        "image/avif" => "pasted-image.avif",
        "image/bmp" => "pasted-image.bmp",
        "image/tiff" => "pasted-image.tiff",
        "image/heic" => "pasted-image.heic",
        "image/heif" => "pasted-image.heif",
        _ => "pasted-image.jpg",
    };

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
        catch (SignOffNotFoundException)
        {
            return Results.NotFound(new { message = "Conversation not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> RevokeSignOffAsync(
        Guid organizationId,
        Guid conversationId,
        Guid messageId,
        RevokeSignOffRequest request,
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

        try
        {
            var message = await conversations.RevokeSignOffAsync(
                organizationId, userId.Value, conversationId, messageId, request.Reason, cancellationToken);
            return Results.Ok(message);
        }
        catch (SignOffNotFoundException)
        {
            return Results.NotFound(new { message = "Conversation not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> UploadAttachmentAsync(
        Guid organizationId,
        Guid conversationId,
        HttpRequest request,
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

        byte[]? bytes = null;
        string? declaredType = null;
        string? fileName = null;
        int? width = null;
        int? height = null;

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "A file is required." });
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
            declaredType = file.ContentType;
            fileName = file.FileName;
        }
        else
        {
            var payload = await request.ReadFromJsonAsync<UploadAttachmentRequest>(cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.ImageData))
            {
                return Results.BadRequest(new { message = "Attachment data is required." });
            }

            var raw = payload.ImageData.Trim();
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = raw.IndexOf(',');
                if (comma <= 0)
                {
                    return Results.BadRequest(new { message = "Attachment data is required." });
                }
                declaredType = raw[5..comma].Split(';')[0];
                raw = raw[(comma + 1)..];
            }

            try
            {
                bytes = Convert.FromBase64String(raw);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { message = "Attachment data is not valid base64." });
            }

            fileName = payload.FileName;
            width = payload.Width;
            height = payload.Height;
        }

        if (bytes is null || bytes.Length == 0)
        {
            return Results.BadRequest(new { message = "Attachment data is required." });
        }

        // Over the cap before anything is written: a rejected upload is never stored.
        if (bytes.LongLength > MediaContentTypes.MaxFileBytes)
        {
            return Results.BadRequest(new
            {
                message = $"An attachment may be at most {MediaContentTypes.MaxFileBytes / (1024 * 1024)} MB.",
            });
        }

        var contentType = AttachmentContentPolicy.ResolveForStorage(declaredType, fileName, bytes);
        if (contentType is null)
        {
            return Results.BadRequest(new { message = "Only images and PDFs can be attached." });
        }

        var stored = await conversations.CreateAttachmentAsync(
            organizationId, userId.Value, conversationId, bytes, contentType,
            string.IsNullOrWhiteSpace(fileName) ? "attachment" : Path.GetFileName(fileName),
            width, height, cancellationToken);

        return stored is null
            ? Results.NotFound(new { message = "Conversation not found." })
            : Results.Ok(AttachmentDto.From(stored));
    }

    private static async Task<IResult> GetAttachmentAsync(
        Guid organizationId,
        Guid conversationId,
        Guid attachmentId,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var attachment = await conversations.GetAttachmentAsync(
            organizationId, userId.Value, conversationId, attachmentId, cancellationToken);
        if (attachment is null)
        {
            return Results.NotFound(new { message = "Attachment not found." });
        }

        var stream = await conversations.OpenAttachmentAsync(attachment, cancellationToken);
        if (stream is null)
        {
            return Results.NotFound(new { message = "Attachment not found." });
        }

        // The stored content type comes from the uploader, so never let the browser sniff or
        // render a non-allow-listed payload from this origin.
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentDisposition = "inline";
        return Results.File(stream, MediaContentTypes.SafeServe(attachment.ContentType));
    }

    private static async Task<IResult> MarkReadAsync(
        Guid organizationId,
        Guid conversationId,
        MarkConversationReadRequest request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        if (request.LastReadMessageId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "A lastReadMessageId is required." });
        }

        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var outcome = await conversations.MarkReadAsync(
            organizationId, userId.Value, conversationId, request.LastReadMessageId, cancellationToken);

        return outcome switch
        {
            // A write that would move the marker backwards is not an error: the marker is
            // already further along, and the caller's intent is satisfied either way.
            MarkReadOutcome.Recorded or MarkReadOutcome.Ignored => Results.NoContent(),
            MarkReadOutcome.ConversationNotFound =>
                Results.NotFound(new { message = "Conversation not found." }),
            _ => Results.BadRequest(new { message = "The message does not belong to this conversation." }),
        };
    }

    private static async Task<IResult> SelectCustomerAsync(
        Guid organizationId,
        Guid conversationId,
        SelectCustomerRequest request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IMessageBroadcaster broadcaster,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        if (request.CustomerId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "A customerId is required." });
        }

        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var conversation = await conversations.SelectCustomerAsync(
            organizationId, userId.Value, conversationId, request.CustomerId, request.Query, cancellationToken);

        if (conversation is null)
        {
            return Results.NotFound(new { message = "Conversation not found." });
        }

        // Binding a customer changes the row's name and unpins a channel thread, so the tile is
        // broadcast from the site that made the change.
        await BroadcastTileAsync(conversations, broadcaster, conversation.Id, cancellationToken);
        return Results.Ok(conversation);
    }

    /// <summary>
    /// Sends a block's words to the thread's customer on their own channel.
    /// </summary>
    /// <remarks>
    /// The status codes are the refusal taxonomy, because each one is a different thing for the
    /// associate to do next: <c>404</c> the thread moved on, <c>409</c> this thread or this tenant
    /// cannot deliver at all, <c>502</c> the provider was reached and said no. Every refusal body
    /// carries the sentence to show, so the client never has to invent wording for a state the
    /// server understands better than it does.
    /// </remarks>
    private static async Task<IResult> DeliverToCustomerAsync(
        Guid organizationId,
        Guid conversationId,
        DeliverToCustomerRequest request,
        ClaimsPrincipal user,
        ICustomerDeliveryService delivery,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { message = "There was nothing to send." });
        }

        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var outcome = await delivery.DeliverAsync(
            organizationId, userId.Value, conversationId, request.Text,
            request.ClientMessageId, cancellationToken);

        if (outcome.Delivered)
        {
            return Results.Ok(new DeliveryResultDto(
                true, outcome.Channel, outcome.ProviderMessageId, outcome.Message));
        }

        var body = new DeliveryResultDto(
            false, Refusal: ToWireRefusal(outcome.Refusal), Detail: outcome.Detail);

        return outcome.Refusal switch
        {
            DeliveryRefusal.ConversationNotFound => Results.NotFound(body),
            DeliveryRefusal.ProviderRefused => Results.Json(body, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Json(body, statusCode: StatusCodes.Status409Conflict),
        };
    }

    /// <summary>Re-runs the agent for the turn behind a message. The fresh blocks arrive on the hub.</summary>
    private static async Task<IResult> RegenerateAsync(
        Guid organizationId,
        Guid conversationId,
        Guid messageId,
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

        var started = await conversations.RegenerateAsync(
            organizationId, userId.Value, conversationId, messageId, cancellationToken);

        if (!started)
        {
            return Results.NotFound(new { message = "That message is not in this conversation." });
        }

        // 202, not 200: the run is accepted and the content does not exist yet. It arrives as
        // message events on the Salon hub, exactly like every other agent reply.
        return Results.Accepted();
    }

    /// <summary>The refusal's wire name, so a client can branch on a state instead of on prose.</summary>
    private static string? ToWireRefusal(DeliveryRefusal? refusal) => refusal switch
    {
        DeliveryRefusal.ConversationNotFound => "conversation_not_found",
        DeliveryRefusal.NoCustomer => "no_customer",
        DeliveryRefusal.NoChannelHandle => "no_channel_handle",
        DeliveryRefusal.ChannelNotConnected => "channel_not_connected",
        DeliveryRefusal.ChannelUnsupported => "channel_unsupported",
        DeliveryRefusal.ProviderRefused => "provider_refused",
        _ => null,
    };

    /// <summary>
    /// Sends a conversation's inbox tile to the clients watching its list. A tile this instance
    /// cannot resolve is skipped rather than failing the request that changed the row.
    /// </summary>
    private static async Task BroadcastTileAsync(
        IConversationService conversations,        IMessageBroadcaster broadcaster,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var tile = await conversations.GetTileAsync(conversationId, cancellationToken);
        if (tile is not null)
        {
            await broadcaster.BroadcastConversationChangedAsync(tile, cancellationToken);
        }
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
