using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Attachments;

/// <summary>
/// Stores message attachments in the row itself (Postgres <c>bytea</c>), following the catalog's
/// shipped precedent for product images.
/// </summary>
/// <remarks>
/// This is the local provider, not a permanent choice: byte growth that outgrows the database's
/// backup/restore budget, or a media volume where serving through the API becomes the
/// bottleneck, is what the CDN adapter exists to answer. The reserved
/// <c>StorageProvider</c>/<c>StorageKey</c> columns and the stored <c>Url</c> are what keep that
/// swap to a new adapter plus a config value.
/// </remarks>
public sealed class DatabaseAttachmentStore : IAttachmentStore
{
    private readonly AppDbContext _context;

    public DatabaseAttachmentStore(AppDbContext context)
    {
        _context = context;
    }

    public string Provider => "database";

    public async Task<MessageAttachment> StoreAsync(
        AttachmentStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        var attachment = new MessageAttachment
        {
            OrganizationId = request.OrganizationId,
            ConversationId = request.ConversationId,
            UploadedByUserId = request.UploadedByUserId,
            StorageProvider = Provider,
            ImageData = request.Bytes,
            ContentType = request.ContentType,
            FileName = request.FileName,
            SizeBytes = request.Bytes.LongLength,
            Width = request.Width,
            Height = request.Height,
            ContentHash = request.ContentHash,
            CreatedAtUtc = DateTime.UtcNow,
        };

        // The key is the row id, and the URL is the authenticated route that serves it: the
        // bytes are never anonymous, so the URL carries no token and resolves nothing on its own.
        attachment.StorageKey = attachment.Id.ToString();
        attachment.Url =
            $"/api/v1/orgs/{request.OrganizationId}/conversations/{request.ConversationId}/attachments/{attachment.Id}";

        _context.MessageAttachments.Add(attachment);
        await _context.SaveChangesAsync(cancellationToken);

        return attachment;
    }

    public Task<Stream?> OpenReadAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        var bytes = attachment.ImageData;
        if (bytes is null || bytes.Length == 0)
        {
            return Task.FromResult<Stream?>(null);
        }
        return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        // The bytes live in the row, so the caller's row delete is the delete.
        return Task.CompletedTask;
    }
}
