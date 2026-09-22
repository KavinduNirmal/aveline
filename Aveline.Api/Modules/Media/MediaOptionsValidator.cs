using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The startup fail-fast rules for the media surface (strategy §3.4), following the precedent of
/// <c>TelemetrySecurityGuard</c> and <c>MetricsSecurityGuard</c>: a half-configured provider is a
/// boot error, not a first-request error. The app starts on the safe default
/// (<c>Media:Provider=database</c>) with no media configuration at all.
/// </summary>
public static class MediaOptionsValidator
{
    /// <summary>
    /// The retention key, read directly because S7's job lives in the Conversations module and
    /// this unit does not own that module's options type.
    /// </summary>
    public const string AttachmentRetentionDaysKey = "Conversations:AttachmentRetentionDays";

    /// <summary>The documented default retention window, in days (strategy §3.4).</summary>
    public const int DefaultAttachmentRetentionDays = 7;

    private const int SigningKeyBytes = 32;

    /// <summary>
    /// Validates the whole surface and throws <see cref="InvalidOperationException"/> on the
    /// first rule that cannot hold. <paramref name="logger"/> is optional and is used only for
    /// the accepted Production override, which logs at <see cref="LogLevel.Warning"/>.
    /// </summary>
    public static void ValidateOrThrow(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration
            .GetSection(MediaOptions.SectionName)
            .Get<MediaOptions>() ?? new MediaOptions();

        // Provider-neutral rules first, so the message names the actual mistake.
        ValidateCatalogDisplayWidth(options);
        ValidateAttachmentRetention(configuration);
        ValidateImageUrlAllowlist(options);

        if (options.Provider == MediaProvider.Cloudinary)
        {
            // Resolving first proves the credential is complete before the key/URL rules run,
            // so a half-configured provider reports the credential, not a downstream symptom.
            _ = ResolveCloudinaryCredentials(configuration);
            ValidateSigningKey(options);
            ValidatePublicBaseUrl(options);
            return;
        }

        ValidateProductionDatabaseOverride(options, environment, logger);
    }

    private static CloudinaryCredentials ResolveCloudinaryCredentials(IConfiguration configuration)
    {
        if (!CloudinaryUrlParser.TryResolve(configuration, out var credentials, out var error))
        {
            throw new InvalidOperationException(
                "Media:Provider=cloudinary but no Cloudinary credential could be resolved. Set "
                + "CLOUDINARY_URL (cloudinary://<key>:<secret>@<cloud_name>), or set both "
                + "CLOUDINARY_API_KEY and CLOUDINARY_API_SECRET (with CLOUDINARY_CLOUD_NAME). "
                + (error ?? string.Empty));
        }

        if (string.IsNullOrWhiteSpace(credentials.CloudName)
            || string.IsNullOrWhiteSpace(credentials.ApiKey)
            || string.IsNullOrWhiteSpace(credentials.ApiSecret))
        {
            throw new InvalidOperationException(
                "Media:Provider=cloudinary but the resolved Cloudinary credential is incomplete: "
                + "CloudName, ApiKey and ApiSecret must all be non-empty. Set CLOUDINARY_URL, or "
                + "the discrete CLOUDINARY_API_KEY / CLOUDINARY_API_SECRET / CLOUDINARY_CLOUD_NAME.");
        }

        return credentials;
    }

    private static void ValidateSigningKey(MediaOptions options)
    {
        var encoded = options.SigningKey;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException(
                "Media:SigningKey must be configured when Media:Provider=cloudinary. Set it to a "
                + "base64-encoded 32-byte key (generate with `openssl rand -base64 32`).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Media:SigningKey must be a valid base64 string.");
        }

        if (key.Length != SigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Media:SigningKey must decode to exactly {SigningKeyBytes} bytes.");
        }
    }

    private static void ValidatePublicBaseUrl(MediaOptions options)
    {
        var value = options.PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Media:PublicBaseUrl must be an absolute http(s) URL when "
                + "Media:Provider=cloudinary. The token URLs handed to an external fetcher are "
                + "built from it, so a relative value would be unusable.");
        }
    }

    private static void ValidateCatalogDisplayWidth(MediaOptions options)
    {
        if (options.CatalogDisplayWidth <= 0)
        {
            throw new InvalidOperationException(
                "Media:CatalogDisplayWidth must be a positive pixel width; 0 (or less) is a bug, "
                + "not a default. Exactly one width is delivered (strategy §3.4, §3.7).");
        }
    }

    private static void ValidateAttachmentRetention(IConfiguration configuration)
    {
        var raw = configuration[AttachmentRetentionDaysKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return; // the documented default applies.
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
        {
            throw new InvalidOperationException(
                $"{AttachmentRetentionDaysKey} must be an integer number of days.");
        }

        if (days <= 0)
        {
            throw new InvalidOperationException(
                $"{AttachmentRetentionDaysKey} must be greater than zero; it is the window after "
                + "which a bound conversation attachment and its remote asset are deleted "
                + "(strategy §3.4, S7).");
        }
    }

    private static void ValidateImageUrlAllowlist(MediaOptions options)
    {
        var malformed = MediaOptions.FindMalformedAllowlistEntry(options.ImageUrlAllowlist);
        if (malformed is not null)
        {
            throw new InvalidOperationException(
                $"Media:ImageUrlAllowlist contains a malformed entry '{malformed}'. The list is "
                + "comma-separated host names with no scheme, port or path; leave it empty to "
                + "allow any public host.");
        }
    }

    private static void ValidateProductionDatabaseOverride(
        MediaOptions options, IHostEnvironment environment, ILogger? logger)
    {
        var isProduction = string.Equals(
            environment.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);

        if (!isProduction)
        {
            return;
        }

        if (!options.AllowDatabaseProviderInProduction)
        {
            throw new InvalidOperationException(
                "Media:Provider=database is refused in Production. Set Media:Provider=cloudinary, "
                + "or set Media:AllowDatabaseProviderInProduction=true to accept that image bytes "
                + "are stored in the database (the documented rollback escape hatch, Q11).");
        }

        logger?.LogWarning(
            "Media:Provider=database is running in Production because "
            + "Media:AllowDatabaseProviderInProduction=true. Image bytes are stored in the "
            + "database; this is the rollback escape hatch and should be temporary.");
    }
}
