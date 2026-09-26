namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// The refusal a pasted-image-URL fetch raises. It is typed so the caller can distinguish "this
/// URL is not acceptable" (map to a <c>400</c> that tells the client to upload the file instead)
/// from a transport failure, without parsing a message.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is deliberately static and contains no part of the input URL.
/// The URL is caller-controlled and may carry a token in its query string; echoing it into an
/// exception that a caller might surface in a response, or a logger might record, is exactly the
/// leak salon §7.5 item 13 forbids. The host and a hash travel on the log line, never here.
/// </remarks>
public sealed class ImageUrlFetchException : Exception
{
    public ImageUrlFetchException(string reason, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>One of the <see cref="ImageUrlFetchReasons"/> codes.</summary>
    public string Reason { get; }
}

/// <summary>
/// The refusal codes. They are stable strings rather than an enum because they are logged and
/// may be matched by a caller; adding one must not renumber an existing one.
/// </summary>
public static class ImageUrlFetchReasons
{
    /// <summary><c>Media:ImageUrlUploadEnabled=false</c>: the feature is off.</summary>
    public const string Disabled = "feature-disabled";

    /// <summary>The value is not an absolute URL.</summary>
    public const string Unparseable = "unparseable";

    /// <summary>The scheme is not <c>https</c> (nor <c>http</c> when explicitly allowed).</summary>
    public const string SchemeNotAllowed = "scheme-not-allowed";

    /// <summary><c>http</c> without <c>Media:AllowInsecureImageFetch=true</c>.</summary>
    public const string InsecureSchemeNotAllowed = "insecure-scheme-not-allowed";

    /// <summary>The URL carries user information (<c>user:pass@host</c>).</summary>
    public const string CredentialsNotAllowed = "credentials-not-allowed";

    /// <summary>The port is not the scheme's default (443 for https, 80 for http).</summary>
    public const string PortNotAllowed = "port-not-allowed";

    /// <summary>The host is not on <c>Media:ImageUrlAllowlist</c>.</summary>
    public const string HostNotAllowed = "host-not-allowed";

    /// <summary>The configured allow-list itself is malformed: fail closed.</summary>
    public const string AllowlistMalformed = "allowlist-malformed";

    /// <summary>The host resolved to no address at all.</summary>
    public const string UnresolvableHost = "unresolvable-host";

    /// <summary>At least one resolved address is not public (loopback, private, link-local, ...).</summary>
    public const string NonPublicAddress = "non-public-address";

    /// <summary>More redirects than <c>Media:ImageUrlMaxRedirects</c>.</summary>
    public const string TooManyRedirects = "too-many-redirects";

    /// <summary>A redirect status without a usable <c>Location</c>.</summary>
    public const string InvalidRedirect = "invalid-redirect";

    /// <summary>The fetch did not complete inside <c>Media:ImageUrlFetchTimeoutSeconds</c>.</summary>
    public const string Timeout = "timeout";

    /// <summary>The body exceeded <c>MediaContentTypes.MaxFileBytes</c>.</summary>
    public const string TooLarge = "too-large";

    /// <summary>The response declared a content type that is not on the image allow-list.</summary>
    public const string ContentTypeNotAllowed = "content-type-not-allowed";

    /// <summary>The bytes are not a recognised image, whatever the header claimed.</summary>
    public const string ContentSignatureMismatch = "content-signature-mismatch";

    /// <summary>The transport failed, or a non-2xx status was returned.</summary>
    public const string FetchFailed = "fetch-failed";
}
