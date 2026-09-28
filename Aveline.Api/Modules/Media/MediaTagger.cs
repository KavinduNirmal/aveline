using System.Globalization;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The one builder of a stored asset's tags and context (strategy §3.2, D1). It is I/O-free and
/// takes the instant as an argument, so the whole tag/context contract is unit-testable without a
/// clock, a network, or a provider.
/// </summary>
/// <remarks>
/// <para>
/// There are <b>two</b> entry points, not a tier switch: the catalog write and the salon
/// attachment write have genuinely different inputs and produce different metadata, and a
/// <c>switch (tier)</c> would have to invent the missing fields for one of them.
/// </para>
/// <para>
/// Two rules are structural rather than conventional. <c>fileName</c> never enters
/// <c>context</c>: it is caller-controlled and may contain <c>=</c> or <c>|</c>, so no entry point
/// accepts one at all. <c>contentType</c> never enters <c>context</c>: the <c>kind:image|pdf</c>
/// label carries the only distinction a query needs, and the exact type is on the row. Every
/// value that does reach <c>context</c> is a GUID, an ISO date, a known literal, or one of the
/// three <see cref="MediaSource"/> tokens, so nothing needs escaping.
/// </para>
/// </remarks>
public static class MediaTagger
{
    /// <summary>The label every catalog asset carries.</summary>
    public const string CatalogImageLabel = "catalog-image";

    /// <summary>The label every salon attachment carries.</summary>
    public const string SalonImageLabel = "salon-image";

    /// <summary>The context token for a catalog write; also carried as the <c>s=</c> value.</summary>
    public const string CatalogSource = "catalog";

    /// <summary>The explicit "there is no uploader" value for the inbound channel path (salon §6.5).</summary>
    public const string NoUserId = "none";

    private const string ImageKind = "image";
    private const string PdfKind = "pdf";
    private const string PdfContentType = "application/pdf";

    /// <summary>The canonical serialization order of the context keys (salon §6.3).</summary>
    private static readonly string[] ContextKeyOrder = ["o", "v", "u", "c", "d", "s"];

    /// <summary>
    /// Metadata for a catalog write (<c>CloudinaryInventoryImageStore</c>): four labels and three
    /// context pairs, exactly as the strategy's §3.2 table fixes them.
    /// </summary>
    public static MediaMetadata ForCatalogImage(Guid organizationId, DateTimeOffset utcNow)
    {
        var date = UtcDate(utcNow);

        return new MediaMetadata(
            [
                CatalogImageLabel,
                $"kind:{ImageKind}",
                $"organizationId:{organizationId}",
                $"date:{date}",
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["o"] = organizationId.ToString(),
                ["d"] = date,
                ["s"] = CatalogSource,
            });
    }

    /// <summary>
    /// Metadata for a salon attachment write (<c>CloudinaryAttachmentStore</c>): seven labels and
    /// up to six context pairs (salon §6.3, unchanged by the strategy).
    /// </summary>
    /// <param name="source">The provenance; the only source of the <c>source:</c> label and <c>s=</c>.</param>
    /// <param name="organizationId">The owning organisation.</param>
    /// <param name="conversationId">The conversation the attachment belongs to.</param>
    /// <param name="userId">
    /// The uploader, or <c>null</c> on the inbound channel path — which renders the explicit
    /// <c>userId:none</c> rather than omitting the label (salon §6.5).
    /// </param>
    /// <param name="customerId">
    /// The customer the media is about, when there is one. An absent customer is an absent
    /// <c>c=</c> pair, never an empty one: Cloudinary rejects empty context values.
    /// </param>
    /// <param name="contentType">Used only to choose <c>kind:image</c> versus <c>kind:pdf</c>.</param>
    /// <param name="utcNow">The instant whose UTC day becomes <c>date:yyyy-MM-dd</c>.</param>
    public static MediaMetadata ForSalonAttachment(
        MediaSource source,
        Guid organizationId,
        Guid conversationId,
        Guid? userId,
        Guid? customerId,
        string contentType,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var date = UtcDate(utcNow);
        var kind = IsPdf(contentType) ? PdfKind : ImageKind;
        var user = userId?.ToString() ?? NoUserId;

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["o"] = organizationId.ToString(),
            ["v"] = conversationId.ToString(),
            ["u"] = user,
            ["d"] = date,
            ["s"] = source.Value,
        };

        if (customerId is { } customer)
        {
            attributes["c"] = customer.ToString();
        }

        return new MediaMetadata(
            [
                SalonImageLabel,
                $"source:{source.Value}",
                $"kind:{kind}",
                $"conversationId:{conversationId}",
                $"organizationId:{organizationId}",
                $"userId:{user}",
                $"date:{date}",
            ],
            attributes);
    }

    /// <summary>
    /// Renders the context pairs the way Cloudinary's pipe-separated <c>context</c> parameter is
    /// written, in the canonical order of salon §6.3. Exposed so the worst-case length is asserted
    /// rather than trusted; production passes the dictionary straight to the SDK's map form.
    /// </summary>
    public static string RenderContext(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var pairs = new List<string>(attributes.Count);
        foreach (var key in ContextKeyOrder)
        {
            if (attributes.TryGetValue(key, out var value))
            {
                pairs.Add($"{key}={value}");
            }
        }

        return string.Join('|', pairs);
    }

    /// <summary>
    /// The deterministic catalog public id (strategy §3.3): <c>aveline/{orgId}/catalog/{imageId}</c>.
    /// Determinism is what makes <c>Overwrite=false</c> idempotent; the components are GUIDs, so no
    /// forbidden character (<c>? &amp; # \ % &lt; &gt; +</c>) can appear.
    /// </summary>
    public static string BuildCatalogPublicId(Guid organizationId, Guid imageId) =>
        $"aveline/{organizationId}/catalog/{imageId}";

    /// <summary>
    /// The deterministic conversation public id (strategy §3.3):
    /// <c>aveline/{orgId}/conversations/{attachmentId}</c>.
    /// </summary>
    public static string BuildConversationPublicId(Guid organizationId, Guid attachmentId) =>
        $"aveline/{organizationId}/conversations/{attachmentId}";

    private static string UtcDate(DateTimeOffset utcNow) =>
        utcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool IsPdf(string contentType)
    {
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals(PdfContentType, StringComparison.OrdinalIgnoreCase);
    }
}
