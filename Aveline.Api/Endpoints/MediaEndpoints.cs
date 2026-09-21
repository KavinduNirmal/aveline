using System.Security.Claims;
using Aveline.Api.Common.Media;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

/// <summary>
/// The protected media surface (unit U2.1, lane L1): the streaming token proxy and the two mint
/// endpoints. Mapped once from <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>GET /api/v1/media/{token}</c> is a streaming proxy, never a redirect.</b> A <c>302</c> to
/// a Cloudinary signed URL would be cheaper by one egress hop and would be wrong: the redirect
/// target is a permanent bearer URL, so the caller keeps it and replays it after <c>exp</c>, which
/// makes the time limit decorative. The proxy is the only place <c>exp</c> can be re-checked
/// (migration plan §7.3). The route is anonymous by design — the token <em>is</em> the credential
/// — and answers <c>private, no-store</c>: the catalog route's
/// <c>public, max-age=31536000, immutable</c> must never be copied here (§7.6, G15).
/// </para>
/// <para>
/// The two mints live here rather than in another lane's endpoint file: the conversation mint needs
/// the <c>BoutiqueConversationAccessPolicy</c> and the internal mint the
/// <c>InternalServicePolicy</c>, and neither requires editing <c>ConversationEndpoints.cs</c> or
/// <c>VisualEndpoints.cs</c>. Every mint re-checks that the row belongs to the request's
/// organisation; a cross-org reference is a <c>404</c>, never a token.
/// </para>
/// <para>
/// The token is never logged. The 401 body carries no detail, so an attacker cannot distinguish a
/// wrong key from an expired token from a missing asset by response code (migration plan §7.4,
/// §7.7).
/// </para>
/// </remarks>
public static class MediaEndpoints
{
    /// <summary>The scopes the token route serves: the two tokenised protected scopes.</summary>
    private static readonly MediaScope[] ProtectedScopes =
        [MediaScope.AttachmentView, MediaScope.VisionAnalyze];

    private const string LogCategory = "Aveline.Api.Endpoints.MediaEndpoints";

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/v1/media/{token}", ServeTokenAsync)
            .WithTags("Media")
            .WithName("ServeMediaToken")
            .WithSummary("Stream a protected media asset named by a minted, expiring Aveline token.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status504GatewayTimeout);

        // The internal mint, on both alias prefixes the visual endpoints already answer on.
        endpoints.MapGroup("/internal/visual")
            .WithTags("Visual Intelligence & Sourcing (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy)
            .MapPost("/media-token", MintForReferenceAsync)
            .WithName("MintMediaToken")
            .WithSummary("Mint a single-use vision.analyze token for a referenced asset.")
            .Produces<MediaTokenResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGroup("/api/internal/visual")
            .WithTags("Visual Intelligence & Sourcing (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy)
            .MapPost("/media-token", MintForReferenceAsync)
            .WithName("MintMediaTokenAlias");

        // The member's mint, under the conversation route family and its existing policy.
        endpoints.MapGroup(
                "/api/v1/orgs/{organizationId:guid}/conversations/{conversationId:guid}"
                + "/attachments/{attachmentId:guid}")
            .WithTags("Conversations")
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueConversationAccessPolicy)
            .MapPost("/media-token", MintForConversationAttachmentAsync)
            .WithName("MintConversationAttachmentMediaToken")
            .WithSummary("Mint an attachment.view token for one attachment of one conversation.")
            .Produces<MediaTokenResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    // =======================================================================================
    // GET /api/v1/media/{token}
    // =======================================================================================

    private static async Task<IResult> ServeTokenAsync(
        string token,
        HttpContext context,
        MediaAccessService media,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        // Set before any branch: a protected response is never stored by an intermediary, on a
        // failure as much as on a success.
        context.Response.Headers.CacheControl = "private, no-store";

        var result = await media.OpenAsync(token, ProtectedScopes, cancellationToken);

        if (!result.IsSuccess)
        {
            return Refuse(result, loggerFactory.CreateLogger(LogCategory));
        }

        // The same protected-serve discipline the conversation route already applies: the stored
        // content type comes from the uploader, so never let a browser sniff or render a
        // non-allow-listed payload from this origin.
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentDisposition = "inline";

        return Results.File(
            result.Grant!.Content,
            MediaContentTypes.SafeServe(result.Grant.ContentType));
    }

    private static IResult Refuse(MediaAccessResult result, ILogger logger) => result.Failure switch
    {
        // No detail in the body: the status codes are deliberately uninformative.
        MediaAccessFailure.Malformed
            or MediaAccessFailure.BadSignature
            or MediaAccessFailure.Expired
            or MediaAccessFailure.Replayed =>
            Results.Json(
                new { message = "The media token is not valid." },
                statusCode: StatusCodes.Status401Unauthorized),

        MediaAccessFailure.ScopeMismatch =>
            Results.Json(
                new { message = "The media token is not valid for this resource." },
                statusCode: StatusCodes.Status403Forbidden),

        MediaAccessFailure.NonceStoreUnavailable => ServiceUnavailable(logger, result, "nonce-store"),

        MediaAccessFailure.NotFound =>
            Results.Json(
                new { message = "Media not found." },
                statusCode: StatusCodes.Status404NotFound),

        MediaAccessFailure.ProviderRateLimited =>
            ServiceUnavailable(logger, result, "provider-rate-limit"),

        MediaAccessFailure.ProviderTimeout =>
            GatewayFailure(logger, result, StatusCodes.Status504GatewayTimeout, "provider-timeout"),

        _ => GatewayFailure(logger, result, StatusCodes.Status502BadGateway, "provider-error"),
    };

    private static IResult GatewayFailure(
        ILogger logger, MediaAccessResult result, int statusCode, string reason)
    {
        var correlationId = NewCorrelationId();
        logger.LogError(
            "Media token route refused ({Failure}, {Reason}) correlation {CorrelationId}: {Detail}",
            result.Failure,
            reason,
            correlationId,
            result.ProviderDetail);

        return Results.Json(
            new { message = "The media provider could not serve the asset.", correlationId },
            statusCode: statusCode);
    }

    private static IResult ServiceUnavailable(ILogger logger, MediaAccessResult result, string reason)
    {
        var correlationId = NewCorrelationId();
        logger.LogError(
            "Media token route refused ({Failure}, {Reason}) correlation {CorrelationId}: {Detail}",
            result.Failure,
            reason,
            correlationId,
            result.ProviderDetail);

        return Results.Json(
            new { message = "Media access is temporarily unavailable.", correlationId },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..12];

    // =======================================================================================
    // POST /internal/visual/media-token
    // =======================================================================================

    private static async Task<IResult> MintForReferenceAsync(
        InternalMediaTokenRequest request,
        MediaTokenMintService minter,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.OrganizationId == Guid.Empty
            || request.ImageRefId is not { } imageRefId || imageRefId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                message = "organizationId, imageRefId and imageRefKind are required.",
            });
        }

        // The scope this route may request is exactly `vision.analyze`; a caller cannot obtain an
        // attachment-view grant here (migration plan §7.5).
        var scope = string.IsNullOrWhiteSpace(request.Scope)
            ? MediaScope.VisionAnalyze
            : MediaScopeNames.TryParse(request.Scope);

        if (scope is null)
        {
            return Results.BadRequest(new { message = $"Unknown media scope '{request.Scope}'." });
        }

        if (scope != MediaScope.VisionAnalyze)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var kind = request.ImageRefKind;
        if (kind is not (MediaReferenceKinds.Attachment or MediaReferenceKinds.InventoryImage))
        {
            return Results.BadRequest(new
            {
                message = "imageRefKind must be 'attachment' or 'inventoryImage'.",
            });
        }

        var minted = await minter.MintForReferenceAsync(
            request.OrganizationId, kind, imageRefId, scope.Value, cancellationToken);

        return minted.IsSuccess
            ? Results.Ok(new MediaTokenResponse(
                minted.Token!, minted.Url!, minted.PublicId!, minted.ExpiresAtUtc))
            : Results.NotFound(new { message = "The referenced media was not found." });
    }

    // =======================================================================================
    // POST …/conversations/{id}/attachments/{id}/media-token
    // =======================================================================================

    private static async Task<IResult> MintForConversationAttachmentAsync(
        Guid organizationId,
        Guid conversationId,
        Guid attachmentId,
        ConversationMediaTokenRequest? request,
        ClaimsPrincipal user,
        IConversationService conversations,
        IUserRepository users,
        MediaTokenMintService minter,
        CancellationToken cancellationToken = default)
    {
        // This route serves `attachment.view` only. Naming any other scope is a caller error and
        // is refused before the row is even read.
        if (!string.IsNullOrWhiteSpace(request?.Scope)
            && !string.Equals(
                request.Scope,
                MediaScopeNames.WireName(MediaScope.AttachmentView),
                StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        // Visibility is checked on the conversation and the row is tenant-scoped: a cross-org
        // reference is a 404 and never mints (migration plan §7.5).
        var attachment = await conversations.GetAttachmentAsync(
            organizationId, userId.Value, conversationId, attachmentId, cancellationToken);

        if (attachment is null)
        {
            return Results.NotFound(new { message = "Attachment not found." });
        }

        var minted = minter.Mint(
            organizationId,
            MediaStorageKey.AssetKey(attachment.StorageKey, attachment.Id),
            MediaScope.AttachmentView);

        return Results.Ok(new MediaTokenResponse(
            minted.Token!, minted.Url!, minted.PublicId!, minted.ExpiresAtUtc));
    }

    private static async Task<Guid?> ResolveUserIdAsync(
        ClaimsPrincipal user,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? user.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(clerkId))
        {
            return null;
        }

        var dbUser = await users.GetByClerkIdAsync(clerkId, cancellationToken);
        return dbUser?.Id;
    }
}
