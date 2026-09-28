namespace Aveline.Api.Modules.Media;

/// <summary>
/// The frozen media configuration surface (strategy §3.4). Every key defaults to the safe
/// value, so the app boots with no media configuration at all; <see cref="MediaOptionsValidator"/>
/// refuses only the combinations that cannot work.
/// </summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>Where bytes live. <c>database</c> is the safe default; Production must opt in.</summary>
    public MediaProvider Provider { get; set; } = MediaProvider.Database;

    /// <summary>Per-row cutover: honour a Cloudinary reference on a row that carries one.</summary>
    public bool ReadFromCloudinary { get; set; }

    /// <summary>
    /// Phase 1 dual-write. Default <c>false</c>: keeping every byte in the database defeats the
    /// point of the migration (strategy §0.1).
    /// </summary>
    public bool DualWrite { get; set; }

    /// <summary>Base64, 32 bytes; required once tokens can be minted (<c>Provider=cloudinary</c>).</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Absolute origin used to build the token URLs handed to external fetchers.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The documented rollback escape hatch (Q11 approved): allows <c>database</c> in
    /// Production, and logs at <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/>.
    /// </summary>
    public bool AllowDatabaseProviderInProduction { get; set; }

    /// <summary>Default TTL for a <see cref="MediaScope.VisionAnalyze"/> token.</summary>
    public int VisionTokenTtlSeconds { get; set; } = 600;

    /// <summary>Default TTL for a <see cref="MediaScope.AttachmentView"/> token.</summary>
    public int AttachmentTokenTtlSeconds { get; set; } = 900;

    /// <summary>How much clock skew an <c>exp</c> check tolerates.</summary>
    public int ClockSkewToleranceSeconds { get; set; } = 30;

    /// <summary>The Option-C′ fallback for the agent path; off by default (migration §7.3).</summary>
    public bool VisionUsePrivateDownload { get; set; }

    /// <summary>Whether a pasted image URL may be fetched at all. Safe default: off (Q7 deferred).</summary>
    public bool ImageUrlUploadEnabled { get; set; }

    /// <summary>Optional comma-separated host allow-list; empty means "any public host".</summary>
    public string ImageUrlAllowlist { get; set; } = string.Empty;

    /// <summary>Maximum redirects the URL fetcher follows, re-validating each hop.</summary>
    public int ImageUrlMaxRedirects { get; set; } = 2;

    /// <summary>Total fetch budget for a pasted image URL, in seconds.</summary>
    public int ImageUrlFetchTimeoutSeconds { get; set; } = 10;

    /// <summary>Whether <c>http</c> is tolerated; <c>false</c> keeps the fetcher on HTTPS.</summary>
    public bool AllowInsecureImageFetch { get; set; }

    /// <summary>
    /// Per-file cap for the catalog tier: 2 MB. Tighter than the 5 MB attachment cap because
    /// the catalog has a tighter volume budget (strategy §3.4, §R8).
    /// </summary>
    public long CatalogMaxFileBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Exactly one delivery width; a variant of zero is a bug, not a default (§3.7).</summary>
    public int CatalogDisplayWidth { get; set; } = 800;

    /// <summary>The allow-list entries, trimmed and with empties removed.</summary>
    public IReadOnlyList<string> ImageUrlAllowlistEntries() =>
        ImageUrlAllowlist
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

    /// <summary>
    /// The first malformed <c>Media:ImageUrlAllowlist</c> entry, or <c>null</c> when every entry
    /// is a well-formed host name. Empty means "any public host".
    /// </summary>
    public static string? FindMalformedAllowlistEntry(string? allowlist)
    {
        if (string.IsNullOrWhiteSpace(allowlist))
        {
            return null;
        }

        foreach (var raw in allowlist.Split(','))
        {
            var entry = raw.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (!IsWellFormedHost(entry))
            {
                return entry;
            }
        }

        return null;
    }

    private static bool IsWellFormedHost(string entry)
    {
        // A host name carries no scheme, port, path, user info or wildcard.
        if (entry.Any(char.IsWhiteSpace)
            || entry.IndexOfAny(['/', ':', '@', '?', '#', '*']) >= 0)
        {
            return false;
        }

        return Uri.CheckHostName(entry) != UriHostNameType.Unknown;
    }
}
