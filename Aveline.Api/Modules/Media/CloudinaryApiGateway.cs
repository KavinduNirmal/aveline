using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The one production implementation of <see cref="ICloudinaryGateway"/>: it owns the SDK client
/// and translates each SDK result into the provider-neutral
/// <see cref="CloudinaryAttemptResult"/> the retry policy reads.
/// </summary>
/// <remarks>
/// <para>
/// The client is constructed from the explicitly resolved credential (strategy §3.4; migration
/// plan C11), never from the SDK's ambient <c>new Cloudinary()</c> environment read.
/// <c>Api.Secure</c> is set here, once — the .NET default is <c>false</c>, and an <c>http</c>
/// delivery URL cannot be fetched by the external vision provider.
/// </para>
/// <para>
/// The SDK does not throw on an HTTP error status; it deserialises the body and reports the status
/// on the result. That is why the status is read from the result, not from an exception, and why
/// a <c>420</c> reaches the retry policy (risk R22).
/// </para>
/// </remarks>
internal sealed class CloudinaryApiGateway : ICloudinaryGateway
{
    private readonly Cloudinary _cloudinary;
    private readonly HttpClient _httpClient;

    public CloudinaryApiGateway(
        HttpClient httpClient,
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryApiGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        var settings = options.Value;

        _cloudinary = new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));

        // Explicit, because the SDK's default is false (strategy §3.4, migration plan C10).
        _cloudinary.Api.Secure = true;
    }

    /// <inheritdoc />
    public bool Secure => _cloudinary.Api.Secure;

    /// <inheritdoc />
    public async Task<CloudinaryAttemptResult> UploadImageAsync(
        ImageUploadParams parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await _cloudinary.UploadAsync(parameters, ct).ConfigureAwait(false);
        return CloudinaryAttemptResultFactory.FromUpload(result);
    }

    /// <inheritdoc />
    public async Task<CloudinaryAttemptResult> UploadRawAsync(
        RawUploadParams parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await _cloudinary.UploadAsync(parameters, cancellationToken: ct).ConfigureAwait(false);
        return CloudinaryAttemptResultFactory.FromUpload(result);
    }

    /// <inheritdoc />
    public async Task<CloudinaryAttemptResult> DestroyAsync(
        DeletionParams parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await _cloudinary.DestroyAsync(parameters).ConfigureAwait(false);
        return CloudinaryAttemptResultFactory.FromDeletion(result);
    }

    /// <inheritdoc />
    public async Task<CloudinaryResourceInfo?> GetResourceAsync(
        string publicId, string resourceType, string deliveryType, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        var parameters = new GetResourceParams(publicId)
        {
            ResourceType = CloudinaryResourceTypes.Parse(resourceType),
            Type = deliveryType,
        };

        var result = await _cloudinary.GetResourceAsync(parameters, ct).ConfigureAwait(false);
        if (result.Error is not null || (int)result.StatusCode is < 200 or >= 300)
        {
            return null;
        }

        var tags = result.Tags ?? [];
        var derived = (result.Derived ?? [])
            .Select(derivative => derivative.Format ?? derivative.Transformation ?? string.Empty)
            .ToArray();

        return new CloudinaryResourceInfo(tags, derived);
    }

    /// <inheritdoc />
    public string BuildDeliveryUrl(string publicId, string resourceType, string deliveryType, bool signed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        return _cloudinary.Api.Url
            .ResourceType(resourceType)
            .Action("upload")
            .Type(deliveryType)
            .Signed(signed)
            .BuildUrl(publicId);
    }

    /// <inheritdoc />
    public string BuildPrivateDownloadUrl(
        string publicId, string resourceType, string deliveryType, DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        return _cloudinary.DownloadPrivate(
            publicId,
            attachment: false,
            format: string.Empty,
            type: deliveryType,
            expiresAt: expiresAtUtc.ToUnixTimeSeconds(),
            resourceType: resourceType);
    }

    /// <inheritdoc />
    public async Task<Stream> FetchAsync(string url, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MediaStorageException(
                (int)response.StatusCode,
                $"The provider returned {(int)response.StatusCode} for the signed delivery URL.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }
}

/// <summary>Maps the SDK's result hierarchy onto <see cref="CloudinaryAttemptResult"/>.</summary>
internal static class CloudinaryAttemptResultFactory
{
    /// <summary>Reads one upload answer, including <c>existing</c> (risk R5).</summary>
    public static CloudinaryAttemptResult FromUpload(UploadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new CloudinaryAttemptResult(
            (int)result.StatusCode,
            result.Error?.Message,
            result.PublicId,
            result.SecureUrl?.ToString(),
            result.Url?.ToString(),
            result.Bytes,
            result.JsonObj?["existing"]?.ToObject<bool>() ?? false);
    }

    /// <summary>Reads one destroy answer.</summary>
    public static CloudinaryAttemptResult FromDeletion(DeletionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new CloudinaryAttemptResult(
            (int)result.StatusCode,
            result.Error?.Message,
            null,
            null,
            null,
            0,
            Existing: false);
    }
}
