namespace Aveline.Api.Modules.Media;

/// <summary>
/// A provider-seam failure the caller cannot retry away: an exhausted retry budget, or a
/// non-retryable provider status such as <c>400</c>. The message is built from a value the
/// account secret has already been scrubbed from (risk R24).
/// </summary>
public sealed class MediaStorageException : Exception
{
    /// <summary>A failure that carries no provider status code (e.g. a malformed stored key).</summary>
    public MediaStorageException(string message)
        : base(message)
    {
    }

    /// <summary>A provider answer, with the HTTP status the SDK reported.</summary>
    public MediaStorageException(int statusCode, string message)
        : base(message) => StatusCode = statusCode;

    /// <summary>A transport failure, with the underlying exception preserved.</summary>
    public MediaStorageException(int statusCode, string message, Exception innerException)
        : base(message, innerException) => StatusCode = statusCode;

    /// <summary>The provider's status code, when the failure had one.</summary>
    public int? StatusCode { get; }
}
