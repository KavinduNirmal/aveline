using CloudinaryDotNet.Actions;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The SDK surface <see cref="CloudinaryMediaStorage"/> is written against. It exists so the
/// request mapping and the retry policy are unit-testable without a network: the fake records the
/// SDK's own request objects and answers from a queue. <see cref="CloudinaryApiGateway"/> is the
/// only production implementation.
/// </summary>
/// <remarks>
/// Deliberately narrower than the SDK: five operations, the two URL builders the delivery contract
/// names, and the read-back the live suite needs. It is not the seam's public contract — the seam
/// is <see cref="IMediaStorage"/> — and it never leaves the module.
/// </remarks>
internal interface ICloudinaryGateway
{
    /// <summary>
    /// The SDK's secure flag. The .NET default is <c>false</c>, and an <c>http</c> delivery URL is
    /// refused by the provider-fetchability rule (strategy §3.4), so production sets it explicitly.
    /// </summary>
    bool Secure { get; }

    /// <summary>The image upload API.</summary>
    Task<CloudinaryAttemptResult> UploadImageAsync(ImageUploadParams parameters, CancellationToken ct);

    /// <summary>The raw upload API, which is where a PDF goes.</summary>
    Task<CloudinaryAttemptResult> UploadRawAsync(RawUploadParams parameters, CancellationToken ct);

    /// <summary>The destroy API.</summary>
    Task<CloudinaryAttemptResult> DestroyAsync(DeletionParams parameters, CancellationToken ct);

    /// <summary>Reads an asset's tags and derivatives back; <c>null</c> when it is gone.</summary>
    Task<CloudinaryResourceInfo?> GetResourceAsync(
        string publicId, string resourceType, string deliveryType, CancellationToken ct);

    /// <summary>A delivery URL, signed or not, for the named resource and delivery type.</summary>
    string BuildDeliveryUrl(string publicId, string resourceType, string deliveryType, bool signed);

    /// <summary>The expiring private download URL used by the server-side fetch path.</summary>
    string BuildPrivateDownloadUrl(
        string publicId, string resourceType, string deliveryType, DateTimeOffset expiresAtUtc);

    /// <summary>Fetches the bytes behind a signed URL, server-side.</summary>
    Task<Stream> FetchAsync(string url, CancellationToken ct);
}
