using Aveline.Api.Common.MultiTenancy;

namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// A file attached to a conversation message.
/// </summary>
/// <remarks>
/// Written unbound (when the composer uploads), then bound to exactly one message by the same
/// idempotent insert that creates the message. Rows still unbound after the TTL are swept, so a
/// picker cancelled mid-flight leaves nothing behind.
///
/// The bytes live behind <c>IAttachmentStore</c>. The <see cref="StorageProvider"/> and
/// <see cref="StorageKey"/> columns are reserved from day one so moving to a CDN (the named
/// future adapter) is a new adapter plus a config value, not a read-path migration:
/// <see cref="Url"/> is what every reader uses.
/// </remarks>
public class MessageAttachment : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>Null until the send binds it to the message it belongs to.</summary>
    public Guid? MessageId { get; set; }

    public Guid? UploadedByUserId { get; set; }

    /// <summary><c>database</c> today; a CDN provider name once that adapter lands.</summary>
    public string StorageProvider { get; set; } = "database";

    /// <summary>The provider's own key. The database adapter stores the row id.</summary>
    public string? StorageKey { get; set; }

    /// <summary>The bytes, for the database provider. A CDN provider leaves this null.</summary>
    public byte[]? ImageData { get; set; }

    public string ContentType { get; set; } = "image/jpeg";

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>
    /// The lowercase-hex SHA-256 of the stored bytes (64 characters), or <c>null</c> on a row
    /// written before the column existed. This is the same identity the Elle workstream consumes
    /// as <c>VisionAnalysis.ImageSha256</c> — one helper, one convention.
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// Where a reader fetches the bytes. The database provider stores the authenticated
    /// conversations attachment URL; a CDN provider stores its own secure URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the send bound this row to its message.</summary>
    public DateTime? BoundAtUtc { get; set; }
}
