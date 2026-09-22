using System.Text.Json;
using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.DTOs;

/// <summary>A message as returned to clients. <see cref="ContentBlocks"/> is the parsed
/// JSON block array; <see cref="ContentHash"/> binds a SignOff decision to the exact payload.</summary>
public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    string AuthorKind,
    string? AgentKey,
    Guid? AuthorUserId,
    MessageKind Kind,
    JsonElement ContentBlocks,
    string? ContentHash,
    Guid? ReplyToMessageId,
    MessageStatus Status,
    DateTime CreatedAt,
    /// <summary>
    /// The client-generated idempotency key this message was stored under, echoed so a
    /// client can reconcile the realtime broadcast of its own optimistic row. Null for
    /// anything the server authored.
    /// </summary>
    Guid? ClientMessageId = null,
    /// <summary>The LangGraph run that produced this message, for bubble-to-run correlation.</summary>
    Guid? WorkflowRunId = null)
{
    public static MessageDto From(Message message)
    {
        JsonElement blocks = default;
        if (!string.IsNullOrWhiteSpace(message.ContentBlocksJson))
        {
            try
            {
                blocks = JsonSerializer.Deserialize<JsonElement>(message.ContentBlocksJson);
            }
            catch (JsonException)
            {
                blocks = default;
            }
        }

        return new MessageDto(
            message.Id,
            message.ConversationId,
            message.AuthorKind.ToString(),
            message.AuthorAgentKey,
            message.AuthorUserId,
            message.Kind,
            blocks,
            message.ContentHash,
            message.ReplyToMessageId,
            message.Status,
            message.CreatedAt,
            message.ClientMessageId,
            message.WorkflowRunId);
    }
}

/// <summary>A paginated page of messages.</summary>
public sealed record MessagePage(
    IReadOnlyList<MessageDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>Request to send a staff note (and trigger the agent).</summary>
/// <param name="Text">The note's words. Required and non-blank.</param>
/// <param name="ClientMessageId">
/// Optional client-generated idempotency key (UUIDv4), stable for one composed message across the
/// send and every retry. A replay with the same key and the same text returns the stored message
/// with <c>200</c>; the same key with different text is a <c>409</c>.
/// </param>
/// <param name="AttachmentIds">
/// Optional ids of attachments uploaded for this conversation and not yet bound. All of them
/// must belong to this conversation and be unbound, and there may be at most
/// <c>MediaContentTypes.MaxPerMessage</c> of them; a bad id fails the whole send rather than
/// half-binding.
/// </param>
/// <param name="ImageUrl">
/// Optional absolute URL of an image the server should fetch and attach. The path is explicit
/// rather than scanned out of <paramref name="Text"/>: a regex would fire on a product link and
/// miss <c>…/image?id=123</c>, and it would turn a typo into a server-side request (salon plan
/// §7.1). The fetch is best-effort — a refused URL leaves the text intact and adds no attachment —
/// except when the feature is disabled, which is an explicit <c>400</c>.
/// </param>
public sealed record SendMessageRequest(
    string Text,
    Guid? ClientMessageId = null,
    IReadOnlyList<Guid>? AttachmentIds = null,
    string? ImageUrl = null);

/// <summary>
/// Request to revoke an approved SignOff. <see cref="Reason"/> is optional and is only
/// logged, because the immutable decision row records who and when, not why.
/// </summary>
public sealed record RevokeSignOffRequest(string? Reason = null);

/// <summary>An uploaded attachment, as the upload route returns it.</summary>
public sealed record AttachmentDto(
    Guid AttachmentId,
    string Url,
    string ContentType,
    string FileName,
    long SizeBytes,
    int? Width,
    int? Height,
    Guid? MessageId)
{
    public static AttachmentDto From(Models.MessageAttachment attachment) => new(
        attachment.Id,
        attachment.Url,
        attachment.ContentType,
        attachment.FileName,
        attachment.SizeBytes,
        attachment.Width,
        attachment.Height,
        attachment.MessageId);
}

/// <summary>
/// An upload sent as JSON rather than multipart, mirroring the catalog's two accepted shapes.
/// <see cref="ImageData"/> is either raw base64 or a `data:` URL.
/// </summary>
public sealed record UploadAttachmentRequest(
    string ImageData,
    string? FileName = null,
    int? Width = null,
    int? Height = null);

/// <summary>Request to advance the caller's read marker for a conversation.</summary>
public sealed record MarkConversationReadRequest(Guid LastReadMessageId);

/// <summary>
/// Request to decide a human-in-the-loop SignOff message. <see cref="ContentHash"/> is the
/// canonical hash of the content blocks the human saw; the server rejects the decision if the
/// message content no longer hashes to this value (i.e. it changed after display).
/// </summary>
public sealed record SignOffDecisionRequest(bool Approved, string ContentHash);
