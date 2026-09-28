using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Media;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Conversations.Attachments;

/// <summary>
/// Stores message attachments in Cloudinary, writing the <see cref="MessageAttachment"/> row
/// itself. It implements the row seam <see cref="IAttachmentStore"/> and <em>delegates every
/// byte</em> to <see cref="IMediaStorage"/>; no class implements two seams (strategy §3.1).
/// </summary>
/// <remarks>
/// <para>
/// The deterministic public id is the tagger's (<see cref="MediaTagger.BuildConversationPublicId"/>),
/// so a re-upload under <c>Overwrite=false</c> answers with the existing asset rather than
/// replacing it. The seven labels and the <c>context</c> record are built by the one builder
/// (<see cref="MediaTagger.ForSalonAttachment"/>) and carried on the upload call itself — there is
/// no second tagging pass, which would re-create an untagged window (strategy §3.2, D1).
/// </para>
/// <para>
/// <see cref="MessageAttachment.Url"/> deliberately does <strong>not</strong> change: it stays the
/// authenticated Aveline route, because a Cloudinary URL is not a stable value to persist and the
/// client fetches through the API (strategy §3.3). The provider's own
/// <see cref="StoredMedia.StorageKey"/> is what the row carries, verbatim — the provider appends a
/// raw file's extension, so it must not be re-derived.
/// </para>
/// <para>
/// Dual-write is this class's responsibility (migration plan §6.4 step 6): after a successful put,
/// <c>Media:DualWrite</c> writes the bytes to <see cref="MessageAttachment.ImageData"/> as the
/// second copy. Off by default, so the database stops being the asset store (strategy §0.1).
/// </para>
/// </remarks>
public sealed class CloudinaryAttachmentStore : IAttachmentStore
{
    /// <summary>The provider name stored on every row this store writes.</summary>
    public const string ProviderName = "cloudinary";

    private readonly AppDbContext _context;
    private readonly IMediaStorage _storage;
    private readonly MediaOptions _options;
    private readonly TimeProvider _timeProvider;

    public CloudinaryAttachmentStore(
        AppDbContext context,
        IMediaStorage storage,
        IOptions<MediaOptions> options,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public async Task<MessageAttachment> StoreAsync(
        AttachmentStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attachment = new MessageAttachment
        {
            OrganizationId = request.OrganizationId,
            ConversationId = request.ConversationId,
            UploadedByUserId = request.UploadedByUserId,
            StorageProvider = Provider,
            ContentType = request.ContentType,
            FileName = request.FileName,
            SizeBytes = request.Bytes.LongLength,
            Width = request.Width,
            Height = request.Height,
            ContentHash = request.ContentHash,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };

        var publicId = MediaTagger.BuildConversationPublicId(request.OrganizationId, attachment.Id);
        var metadata = MediaTagger.ForSalonAttachment(
            request.Source,
            request.OrganizationId,
            request.ConversationId,
            request.UploadedByUserId,
            request.CustomerId,
            request.ContentType,
            _timeProvider.GetUtcNow());

        var stored = await _storage.PutAsync(
            new MediaPutRequest(
                publicId, request.Bytes, request.ContentType, request.FileName,
                MediaTier.Protected, metadata),
            cancellationToken).ConfigureAwait(false);

        // The provider's key and name are authoritative: for a `raw` resource the provider
        // appends the original file's extension, so the persisted key must be what it returned.
        attachment.StorageProvider = stored.Provider;
        attachment.StorageKey = stored.StorageKey;

        // The URL stays the authenticated Aveline route: `StoredMedia.Url` is deliberately not
        // persisted (strategy §3.3), and every reader uses `MessageAttachment.Url`.
        attachment.Url =
            $"/api/v1/orgs/{request.OrganizationId}/conversations/{request.ConversationId}/attachments/{attachment.Id}";

        // Dual-write's second copy: the same row keeps the bytes, which is stage 1's free rollback
        // (`Media:Provider=database`). Off by default.
        attachment.ImageData = _options.DualWrite ? request.Bytes : null;

        _context.MessageAttachments.Add(attachment);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return attachment;
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        // The dual-write copy wins while it exists: stage 1 keeps every read in the row, so a
        // provider outage is invisible (strategy §5.2 stage 1). With `DualWrite=false` — the
        // default — there is no row copy, and because `Url` stays the authenticated Aveline route
        // the serve path must fetch from the provider instead (strategy §3.3). A database row this
        // adapter did not write also has its bytes on the row.
        if (attachment.ImageData is { Length: > 0 } bytes)
        {
            return new MemoryStream(bytes, writable: false);
        }

        return IsCloudinaryRow(attachment)
            ? await _storage.OpenReadAsync(ReferenceFor(attachment), cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        if (!IsCloudinaryRow(attachment))
        {
            // The bytes live in the row, so the caller's row delete is the delete.
            return;
        }

        await _storage.DeleteAsync(ReferenceFor(attachment), cancellationToken).ConfigureAwait(false);
    }

    private static bool IsCloudinaryRow(MessageAttachment attachment) =>
        string.Equals(attachment.StorageProvider, ProviderName, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(attachment.StorageKey);

    private static StoredMediaRef ReferenceFor(MessageAttachment attachment) =>
        new(attachment.StorageProvider, attachment.StorageKey!, attachment.ContentType);
}
