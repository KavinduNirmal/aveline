using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Attachments;

/// <summary>What an upload asked the store to keep.</summary>
public sealed record AttachmentStoreRequest(
    Guid OrganizationId,
    Guid ConversationId,
    Guid? UploadedByUserId,
    byte[] Bytes,
    string ContentType,
    string FileName,
    int? Width,
    int? Height);

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
