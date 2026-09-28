namespace Aveline.Api.Modules.Media;

/// <summary>
/// One provider call's normalised answer. The SDK returns one result hierarchy per operation and
/// never throws on an HTTP error status, so this record is where the status, the error text and
/// the delivery URLs are read once, before the retry policy decides what to do with them
/// (risk R22).
/// </summary>
/// <param name="StatusCode">The HTTP status the provider reported; <c>420</c> is its rate limit.</param>
/// <param name="ErrorMessage">The provider's error text, unredacted until it is logged.</param>
/// <param name="PublicId">The stored public id, on success.</param>
/// <param name="SecureUrl">The <c>https</c> delivery URL, on success.</param>
/// <param name="Url">The <c>http</c> delivery URL, when the provider sends no secure one.</param>
/// <param name="Bytes">The stored byte count the provider reported.</param>
/// <param name="Existing">
/// Whether the upload answered with the existing asset instead of writing a new one
/// (<c>overwrite=false</c>, risk R5).
/// </param>
internal sealed record CloudinaryAttemptResult(
    int StatusCode,
    string? ErrorMessage,
    string? PublicId,
    string? SecureUrl,
    string? Url,
    long Bytes,
    bool Existing)
{
    /// <summary>Whether the provider accepted the call.</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>
    /// Whether a status is worth another attempt: Cloudinary's rate limit is <c>420</c> (risk R22),
    /// plus the 5xx family. <c>429</c> is deliberately absent — it is not a Cloudinary status, and
    /// retrying it would be a no-op branch.
    /// </summary>
    public static bool IsRetryableStatus(int statusCode) =>
        statusCode == 420 || statusCode is >= 500 and <= 599;
}
