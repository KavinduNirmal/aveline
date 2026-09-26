using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Media;

namespace Aveline.Api.Modules.Conversations.Attachments;

/// <summary>What an upload asked the store to keep.</summary>
/// <remarks>
/// The record is the seam's whole vocabulary: a store implementation that needs something else to
/// route, tag or name its bytes is asking for a field here, not reading the database.
/// </remarks>
/// <param name="OrganizationId">The tenant that owns the bytes. Always present.</param>
/// <param name="ConversationId">The thread the attachment belongs to. Always present.</param>
/// <param name="UploadedByUserId">
/// The staff member who uploaded the bytes, or <c>null</c> for inbound channel media, which no
/// staff device produced.
/// </param>
/// <param name="Bytes">The bytes as received, before any provider upload.</param>
/// <param name="ContentType">The resolved, allow-listed content type.</param>
/// <param name="FileName">The caller's file name. Never a tag or context value.</param>
/// <param name="Width">The image width when the caller knows it; <c>null</c> for a PDF or unknown.</param>
/// <param name="Height">The image height when the caller knows it; <c>null</c> for a PDF or unknown.</param>
/// <param name="Source">
/// Where the bytes came from — <see cref="MediaSource.Web"/> for a staff upload through the Salon,
/// <see cref="MediaSource.WhatsApp"/> for inbound channel media, <see cref="MediaSource.Url"/> for
/// the pasted-URL fetch. Supplied by the call path, which is the only place that knows; it becomes
/// the <c>source:{value}</c> tag and the <c>s=</c> context pair.
/// </param>
/// <param name="CustomerId">
/// The customer the media concerns, when the call path genuinely resolves one (inbound media whose
/// phone number is on file, or a customer-bound Salon); <c>null</c> otherwise. This is the
/// <c>c=</c> context pair, not the uploader.
/// </param>
/// <param name="ContentHash">
/// The byte-level identity of <paramref name="Bytes"/> as lowercase-hex SHA-256, computed with
/// <see cref="AttachmentContentHash.Compute"/>. The Elle workstream consumes the same value as
/// <c>VisionAnalysis.ImageSha256</c>, so there is exactly one convention. <c>null</c> only when a
/// caller cannot supply bytes, which today means no caller.
/// </param>
public sealed record AttachmentStoreRequest(
    Guid OrganizationId,
    Guid ConversationId,
    Guid? UploadedByUserId,
    byte[] Bytes,
    string ContentType,
    string FileName,
    int? Width,
    int? Height,
    MediaSource Source,
    Guid? CustomerId,
    string? ContentHash);

/// <summary>
/// The byte boundary for a message attachment, deliberately small so swapping where the bytes
/// live is total.
/// </summary>
/// <remarks>
/// Implemented today by <see cref="DatabaseAttachmentStore"/> (the bytes sit in the row). The
/// named future adapter is a CDN provider: it would store the bytes elsewhere, fill
/// <see cref="MessageAttachment.StorageProvider"/>/<c>StorageKey</c>/<c>Url</c> with its own
/// values, and return <c>null</c> from <see cref="OpenReadAsync"/> because the client reads the
/// stored <c>Url</c> directly. **No caller branches on the provider**: the upload route calls
/// <see cref="StoreAsync"/> and the serve route calls <see cref="OpenReadAsync"/>.
/// </remarks>
public interface IAttachmentStore
{
    /// <summary>The provider name stored on every row this store writes.</summary>
    string Provider { get; }

    /// <summary>Persists the bytes and returns the row describing them.</summary>
    Task<MessageAttachment> StoreAsync(
        AttachmentStoreRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored bytes, or <c>null</c> when this provider keeps them elsewhere (a CDN adapter)
    /// or the row carries none.
    /// </summary>
    Task<Stream?> OpenReadAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the stored bytes. The caller removes the row.</summary>
    Task DeleteAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken = default);
}
