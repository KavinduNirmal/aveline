using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Media;

/// <summary>The three Cloudinary account values, however they were supplied.</summary>
/// <param name="CloudName">The cloud name (the URL's host).</param>
/// <param name="ApiKey">The API key (the URL's user name).</param>
/// <param name="ApiSecret">The API secret (the URL's password).</param>
public sealed record CloudinaryCredentials(string CloudName, string ApiKey, string ApiSecret);

/// <summary>
/// Parses the Cloudinary SDK's <c>CLOUDINARY_URL</c> convention
/// <c>cloudinary://&lt;key&gt;:&lt;secret&gt;@&lt;cloud_name&gt;</c> <strong>explicitly</strong>, so
/// the credential is a testable input rather than the SDK's ambient global read
/// (strategy §3.4; migration plan C11).
/// </summary>
/// <remarks>
/// The discrete pair <c>CLOUDINARY_API_KEY</c> / <c>CLOUDINARY_API_SECRET</c> is the documented
/// fallback and must be set together; the cloud name for that form comes from
/// <c>CLOUDINARY_CLOUD_NAME</c> (the SDK's third discrete variable) or, failing that, from the
/// <c>Cloudinary:CloudName</c> configuration key.
/// </remarks>
public static class CloudinaryUrlParser
{
    public const string UrlVariable = "CLOUDINARY_URL";
    public const string ApiKeyVariable = "CLOUDINARY_API_KEY";
    public const string ApiSecretVariable = "CLOUDINARY_API_SECRET";
    public const string CloudNameVariable = "CLOUDINARY_CLOUD_NAME";

    private const string Scheme = "cloudinary://";
    private const string Convention =
        "cloudinary://<key>:<secret>@<cloud_name>";

    /// <summary>
    /// Parses one <c>CLOUDINARY_URL</c> value. Returns <c>false</c> with a human-readable
    /// <paramref name="error"/> for anything that is not the exact convention.
    /// </summary>
    public static bool TryParse(
        string? value, out CloudinaryCredentials credentials, out string? error)
    {
        credentials = default!;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{UrlVariable} is empty.";
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            error = $"{UrlVariable} must start with '{Scheme}' ({Convention}).";
            return false;
        }

        var remainder = trimmed[Scheme.Length..];

        // Split on the LAST '@': a secret may contain one, a cloud name may not.
        var at = remainder.LastIndexOf('@');
        if (at < 0)
        {
            error = $"{UrlVariable} is missing the '@<cloud_name>' segment ({Convention}).";
            return false;
        }

        var userInfo = remainder[..at];
        var cloudName = remainder[(at + 1)..];

        var colon = userInfo.IndexOf(':');
        if (colon < 0)
        {
            error = $"{UrlVariable} is missing the ':<secret>' segment ({Convention}).";
            return false;
        }

        var apiKey = userInfo[..colon];
        var apiSecret = userInfo[(colon + 1)..];

        if (apiKey.Length == 0 || apiSecret.Length == 0 || cloudName.Length == 0)
        {
            error = $"{UrlVariable} must carry a non-empty key, secret and cloud name ({Convention}).";
            return false;
        }

        if (cloudName.IndexOfAny(['/', '?', '#']) >= 0)
        {
            error = $"{UrlVariable} has a malformed cloud name.";
            return false;
        }

        credentials = new CloudinaryCredentials(cloudName, apiKey, apiSecret);
        return true;
    }

    /// <summary>
    /// Resolves the credential from configuration: the URL first, then the discrete pair.
    /// Returns <c>false</c> when no credential is configured, or when the configuration is
    /// half-formed (a scheme-less URL, a key without a secret). A present-but-malformed URL never
    /// falls back to the discrete keys, because that would hide a typo.
    /// </summary>
    public static bool TryResolve(
        IConfiguration configuration, out CloudinaryCredentials credentials, out string? error)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var url = configuration[UrlVariable];
        if (!string.IsNullOrWhiteSpace(url))
        {
            return TryParse(url, out credentials, out error);
        }

        var apiKey = configuration[ApiKeyVariable];
        var apiSecret = configuration[ApiSecretVariable];
        var hasKey = !string.IsNullOrWhiteSpace(apiKey);
        var hasSecret = !string.IsNullOrWhiteSpace(apiSecret);

        if (!hasKey && !hasSecret)
        {
            credentials = default!;
            error = null;
            return false;
        }

        if (!hasKey || !hasSecret)
        {
            credentials = default!;
            error = $"{ApiKeyVariable} and {ApiSecretVariable} must be set together (both or neither).";
            return false;
        }

        var cloudName = configuration[CloudNameVariable];
        if (string.IsNullOrWhiteSpace(cloudName))
        {
            cloudName = configuration[$"{CloudinaryOptions.SectionName}:CloudName"];
        }

        credentials = new CloudinaryCredentials(cloudName ?? string.Empty, apiKey!, apiSecret!);
        error = null;
        return true;
    }
}
