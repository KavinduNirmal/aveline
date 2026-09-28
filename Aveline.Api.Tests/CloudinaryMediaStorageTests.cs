using Aveline.Api.Modules.Media;
using CloudinaryDotNet.Actions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.1 (strategy §5.1 S1) — the Cloudinary provider seam. The SDK is reached through
/// <see cref="ICloudinaryGateway"/>, so every request-mapping claim is asserted against the SDK's
/// own request objects (<c>ToParamsDictionary</c>) with no network, and the retry loop is driven
/// by queued outcomes instead of a real 420.
/// </summary>
/// <remarks>
/// Risk R5 (<c>Overwrite=false</c>), risk R17 (the SDK on net10.0) and risk R22 (retry keyed to
/// <c>420</c>, never <c>429</c>) each have their own test here, and risk R24's "the API secret
/// never appears in a log line" is asserted against a capturing <see cref="ILogger"/> on the
/// failure and the retry paths.
/// </remarks>
public class CloudinaryMediaStorageTests
{
    private static readonly Guid Org = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static readonly DateTimeOffset Now =
        new(2026, 9, 21, 9, 30, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------------------------
    // The seam is one seam
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ImplementsOnlyTheProviderSeam()
    {
        typeof(CloudinaryMediaStorage).GetInterfaces().Should().Equal(typeof(IMediaStorage));
    }

    // ---------------------------------------------------------------------------------------
    // Request mapping (strategy §6.3 of the migration plan, §3.2/§3.3 of the strategy)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task PutAsync_CatalogImage_MapsPublicIdDeliveryTypeTagsAndContext()
    {
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);
        var imageId = Guid.NewGuid();
        var publicId = MediaTagger.BuildCatalogPublicId(Org, imageId);
        var metadata = MediaTagger.ForCatalogImage(Org, Now);

        var stored = await storage.PutAsync(
            new MediaPutRequest(publicId, [1, 2, 3], "image/jpeg", "product photo.jpg", MediaTier.Public, metadata));

        stored.Provider.Should().Be("cloudinary");
        stored.StorageKey.Should().Be($"image/upload:{publicId}");

        var parameters = gateway.LastImageUploadParameters!;
        parameters["public_id"].Should().Be(publicId);
        parameters["type"].Should().Be("upload");
        parameters["overwrite"].Should().Be("false");
        parameters["invalidate"].Should().Be("true");
        parameters["tags"].Should().Be(
            $"catalog-image,kind:image,organizationId:{Org},date:2026-09-21");
        parameters["context"].Should().Be($"o={Org}|d=2026-09-21|s=catalog");
        parameters.Should().ContainKey("file");
    }

    [Fact]
    public async Task PutAsync_Pdf_IsARawAuthenticatedUpload()
    {
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);
        var attachmentId = Guid.NewGuid();
        var publicId = MediaTagger.BuildConversationPublicId(Org, attachmentId);
        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.Web, Org, Guid.NewGuid(), Guid.NewGuid(), null, "application/pdf", Now);

        var stored = await storage.PutAsync(
            new MediaPutRequest(publicId, [1, 2, 3], "application/pdf", "invoice.pdf", MediaTier.Protected, metadata));

        gateway.LastImageUploadParameters.Should().BeNull("a PDF must not be uploaded as an image");
        var parameters = gateway.LastRawUploadParameters!;
        parameters["public_id"].Should().Be(publicId);
        parameters["type"].Should().Be("authenticated");
        parameters["overwrite"].Should().Be("false");
        ((string)parameters["tags"]).Should().Contain("kind:pdf");
        // Cloudinary appends the raw file's extension, so the persisted key is the provider's id.
        stored.StorageKey.Should().Be($"raw/authenticated:{publicId}.pdf");
    }

    [Fact]
    public async Task PutAsync_ProtectedImage_IsAnAuthenticatedImageUpload()
    {
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);
        var publicId = MediaTagger.BuildConversationPublicId(Org, Guid.NewGuid());

        var stored = await storage.PutAsync(
            new MediaPutRequest(publicId, [1, 2, 3], "image/jpeg", "photo.jpg", MediaTier.Protected));

        gateway.LastRawUploadParameters.Should().BeNull();
        gateway.LastImageUploadParameters!["type"].Should().Be("authenticated");
        stored.StorageKey.Should().Be($"image/authenticated:{publicId}");
    }

    [Fact]
    public async Task PutAsync_WithNullMetadata_UploadsWithoutTagsOrContext()
    {
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);

        await storage.PutAsync(
            new MediaPutRequest(MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid()), [1, 2, 3],
                "image/jpeg", "photo.jpg", MediaTier.Public, Metadata: null));

        var parameters = gateway.LastImageUploadParameters!;
        parameters.Should().NotContainKey("tags");
        parameters.Should().NotContainKey("context");
    }

    [Fact]
    public async Task PutAsync_FileNameAndContentType_NeverReachTheContext()
    {
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);
        var metadata = MediaTagger.ForCatalogImage(Org, Now);
        const string fileName = "summer=collection|line.jpg";

        await storage.PutAsync(
            new MediaPutRequest(MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid()), [1, 2, 3],
                "image/jpeg", fileName, MediaTier.Public, metadata));

        var context = (string)gateway.LastImageUploadParameters!["context"];
        context.Should().Be(MediaTagger.RenderContext(metadata.Attributes));
        context.Should().NotContain(fileName).And.NotContain("image/jpeg");
    }

    [Fact]
    public async Task PutAsync_LeavesTheCallersBytesIntact_SoARowSeamCanDualWrite()
    {
        // Dual-write's second copy is the row seam's ImageData write (strategy §3.3, migration
        // plan §6.4 step 6). The provider seam must not consume or mutate the caller's array.
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);
        var bytes = new byte[] { 10, 20, 30 };

        await storage.PutAsync(
            new MediaPutRequest(MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid()), bytes,
                "image/jpeg", "photo.jpg", MediaTier.Public));

        bytes.Should().Equal(10, 20, 30);
    }

    [Fact]
    public async Task PutAsync_ReturnsTheSecureDeliveryUrlTheProviderReported()
    {
        var gateway = new FakeCloudinaryGateway(FakeCloudinaryGateway.Success(
            secureUrl: "https://res.cloudinary.com/a-cloud/image/upload/v7/aveline/x"));
        var storage = Storage(gateway);

        var stored = await storage.PutAsync(
            new MediaPutRequest("aveline/x", [1], "image/jpeg", "photo.jpg", MediaTier.Public));

        stored.Url.Should().Be("https://res.cloudinary.com/a-cloud/image/upload/v7/aveline/x");
    }

    // ---------------------------------------------------------------------------------------
    // The delivery contract (strategy §3.3/§3.5) — URL shape, offline
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void PublicDeliveryUrl_ForACatalogRow_IsASecureUploadUrl()
    {
        var storage = Storage(RealGateway());
        var publicId = MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid());

        var url = storage.PublicDeliveryUrl(new StoredMediaRef("cloudinary", $"image/upload:{publicId}", "image/jpeg"));

        url.Should().StartWith("https://res.cloudinary.com/a-cloud/image/upload/");
        url.Should().EndWith(publicId);
        url.Should().NotContain("/authenticated/");
    }

    [Fact]
    public void SignedDeliveryUrl_ForAPublicRow_IsASignedDeliveryUrl()
    {
        var storage = Storage(RealGateway());
        var publicId = MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid());

        var url = storage.SignedDeliveryUrl(
            new StoredMediaRef("cloudinary", $"image/upload:{publicId}", "image/jpeg"),
            Now.AddMinutes(5));

        url.Should().StartWith("https://");
        url.Should().Contain("/image/upload/s--");
    }

    [Fact]
    public void SignedDeliveryUrl_ForAProtectedRow_IsAnExpiringPrivateDownload()
    {
        var storage = Storage(RealGateway());
        var publicId = MediaTagger.BuildConversationPublicId(Org, Guid.NewGuid());

        var url = storage.SignedDeliveryUrl(
            new StoredMediaRef("cloudinary", $"raw/authenticated:{publicId}", "application/pdf"),
            Now.AddMinutes(5));

        url.Should().StartWith("https://api.cloudinary.com/");
        url.Should().Contain("signature=");
        url.Should().Contain("expires_at=");
        url.Should().Contain("type=authenticated");
        url.Should().Contain("/raw/download");
    }

    [Fact]
    public void PublicDeliveryUrl_WithAMalformedStorageKey_IsRefused()
    {
        var storage = Storage(new FakeCloudinaryGateway());

        var act = () => storage.PublicDeliveryUrl(new StoredMediaRef("cloudinary", "not-a-key", "image/jpeg"));

        act.Should().Throw<MediaStorageException>();
    }

    // ---------------------------------------------------------------------------------------
    // Open and delete
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenReadAsync_FetchesTheSignedUrlServerSide()
    {
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);
        var publicId = MediaTagger.BuildConversationPublicId(Org, Guid.NewGuid());

        var stream = await storage.OpenReadAsync(
            new StoredMediaRef("cloudinary", $"image/authenticated:{publicId}", "image/jpeg"));

        stream.Should().NotBeNull();
        gateway.FetchUrls.Should().ContainSingle();
        gateway.FetchUrls[0].Should().Contain(publicId);
    }

    [Fact]
    public async Task DeleteAsync_MapsTheStoredKeyBackToTheProviderRequest()
    {
        var gateway = new FakeCloudinaryGateway();
        var storage = Storage(gateway);
        var publicId = MediaTagger.BuildConversationPublicId(Org, Guid.NewGuid());

        await storage.DeleteAsync(new StoredMediaRef("cloudinary", $"raw/authenticated:{publicId}", "application/pdf"));

        var parameters = gateway.LastDeletionParameters!;
        parameters.PublicId.Should().Be(publicId);
        parameters.Type.Should().Be("authenticated");
        parameters.ResourceType.Should().Be(ResourceType.Raw);
    }

    // ---------------------------------------------------------------------------------------
    // The retry policy — 420 and 5xx only, never 429 (risk R22)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task PutAsync_RetriesOn420_UpToTheConfiguredRetryAttempts()
    {
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(420, "Rate limited"),
            FakeCloudinaryGateway.Failure(420, "Rate limited"),
            FakeCloudinaryGateway.Failure(420, "Rate limited"),
            FakeCloudinaryGateway.Failure(420, "Rate limited"),
            FakeCloudinaryGateway.Failure(420, "Rate limited"));
        var storage = Storage(gateway, retryAttempts: 3);

        var act = () => storage.PutAsync(Request());

        await act.Should().ThrowAsync<MediaStorageException>();
        gateway.UploadImageCallCount.Should().Be(4, "one attempt plus three retries");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task PutAsync_RetriesOnEach5xx(int statusCode)
    {
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(statusCode),
            FakeCloudinaryGateway.Success());
        var storage = Storage(gateway);

        var stored = await storage.PutAsync(Request());

        stored.Provider.Should().Be("cloudinary");
        gateway.UploadImageCallCount.Should().Be(2);
    }

    [Fact]
    public async Task PutAsync_RetriesOn420_ThenSucceeds()
    {
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(420),
            FakeCloudinaryGateway.Success());
        var storage = Storage(gateway);

        await storage.PutAsync(Request());

        gateway.UploadImageCallCount.Should().Be(2);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(413)]
    public async Task PutAsync_DoesNotRetryANonRetryableStatus(int statusCode)
    {
        // 429 is deliberately in this set: Cloudinary signals rate limiting with 420, and a
        // retry keyed to 429 would never fire (risk R22).
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(statusCode),
            FakeCloudinaryGateway.Success());
        var storage = Storage(gateway);

        var act = () => storage.PutAsync(Request());

        await act.Should().ThrowAsync<MediaStorageException>();
        gateway.UploadImageCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PutAsync_BacksOffBetweenAttempts_WithinTheBound()
    {
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(420),
            FakeCloudinaryGateway.Failure(420),
            FakeCloudinaryGateway.Failure(420),
            FakeCloudinaryGateway.Failure(420));
        var delays = new RecordingDelay();
        var storage = Storage(gateway, delay: delays);

        var act = () => storage.PutAsync(Request());

        await act.Should().ThrowAsync<MediaStorageException>();
        delays.Delays.Should().HaveCount(3);
        delays.Delays.Should().OnlyContain(
            delay => delay > TimeSpan.Zero && delay <= TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(1, 1.0, 250)]
    [InlineData(2, 1.0, 500)]
    [InlineData(3, 1.0, 1000)]
    [InlineData(4, 1.0, 2000)]
    [InlineData(5, 1.0, 4000)]
    [InlineData(6, 1.0, 5000)]
    [InlineData(20, 1.0, 5000)]
    [InlineData(1, 0.0, 125)]
    [InlineData(2, 0.0, 250)]
    [InlineData(3, 0.0, 500)]
    public void Backoff_IsExponentialAndBounded(int retryNumber, double jitter, int expectedMilliseconds)
    {
        CloudinaryRetryPolicy.ComputeBackoff(retryNumber, jitter)
            .Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Fact]
    public async Task DeleteAsync_AlsoRetriesOn420()
    {
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(420),
            FakeCloudinaryGateway.Success());
        var storage = Storage(gateway);

        await storage.DeleteAsync(
            new StoredMediaRef("cloudinary", $"image/upload:{MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid())}", "image/jpeg"));

        gateway.DestroyCallCount.Should().Be(2);
    }

    [Fact]
    public async Task PutAsync_RetriesATransientTransportFailure()
    {
        var gateway = new FakeCloudinaryGateway(FakeCloudinaryGateway.Success())
            .ThrowsOnce(new HttpRequestException("connection reset by peer"));
        var storage = Storage(gateway);

        await storage.PutAsync(Request());

        gateway.UploadImageCallCount.Should().Be(2);
    }

    [Fact]
    public async Task PutAsync_WrapsANonTransientSdkFailure_WithoutRetryingOrLeaking()
    {
        const string secret = "cd-api-secret-1b7f";
        var logger = new CapturingLogger<CloudinaryMediaStorage>();
        var gateway = new FakeCloudinaryGateway()
            .ThrowsOnce(new InvalidOperationException($"Failed to deserialize response: {secret}"));
        var storage = Storage(gateway, options: Options(secret), logger: logger);

        var act = () => storage.PutAsync(Request());
        var exception = (await act.Should().ThrowAsync<MediaStorageException>()).Which;

        gateway.UploadImageCallCount.Should().Be(1);
        exception.Message.Should().NotContain(secret);
        // A logger that prints the whole chain must not recover the secret from the inner exception.
        exception.ToString().Should().NotContain(secret);
        logger.AllMessages.Should().NotContain(message => message.Contains(secret, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------
    // The secret never reaches a log line (risk R24)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task PutAsync_OnAFailure_NeverLogsOrThrowsTheApiSecret()
    {
        const string secret = "cd-api-secret-1b7f";
        var logger = new CapturingLogger<CloudinaryMediaStorage>();
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(400, $"Bad request: api_secret={secret} rejected"));
        var storage = Storage(gateway, options: Options(secret), logger: logger);

        var act = () => storage.PutAsync(Request());
        var exception = (await act.Should().ThrowAsync<MediaStorageException>()).Which;

        logger.AllMessages.Should().NotContain(message => message.Contains(secret, StringComparison.Ordinal));
        exception.Message.Should().NotContain(secret);
    }

    [Fact]
    public async Task PutAsync_OnARetryPath_NeverLogsTheApiSecret()
    {
        const string secret = "cd-api-secret-1b7f";
        var logger = new CapturingLogger<CloudinaryMediaStorage>();
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(420, $"Rate limit for api_secret={secret}"),
            FakeCloudinaryGateway.Success());
        var storage = Storage(gateway, options: Options(secret), logger: logger);

        await storage.PutAsync(Request());

        logger.AllMessages.Should().NotContain(message => message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_OnAFailure_NeverLogsTheApiSecret()
    {
        const string secret = "cd-api-secret-1b7f";
        var logger = new CapturingLogger<CloudinaryMediaStorage>();
        var gateway = new FakeCloudinaryGateway(
            FakeCloudinaryGateway.Failure(500, $"boom {secret}"),
            FakeCloudinaryGateway.Failure(500, $"boom {secret}"),
            FakeCloudinaryGateway.Failure(500, $"boom {secret}"),
            FakeCloudinaryGateway.Failure(500, $"boom {secret}"));
        var storage = Storage(gateway, options: Options(secret), logger: logger);

        var act = () => storage.DeleteAsync(
            new StoredMediaRef("cloudinary", $"image/upload:{MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid())}", "image/jpeg"));

        await act.Should().ThrowAsync<MediaStorageException>();
        logger.AllMessages.Should().NotContain(message => message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRealGateway_SetsSecureExplicitly()
    {
        // The .NET SDK's default is false, and a non-secure URL would be http (strategy §3.4).
        RealGateway().Secure.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static MediaPutRequest Request() =>
        new(MediaTagger.BuildCatalogPublicId(Org, Guid.NewGuid()), [1, 2, 3], "image/jpeg",
            "photo.jpg", MediaTier.Public, MediaTagger.ForCatalogImage(Org, Now));

    private static CloudinaryOptions Options(string apiSecret = "test-secret") => new()
    {
        CloudName = "a-cloud",
        ApiKey = "test-key",
        ApiSecret = apiSecret,
        CatalogDeliveryType = "upload",
        ProtectedDeliveryType = "authenticated",
        UploadRetryAttempts = 3,
    };

    private static CloudinaryApiGateway RealGateway() =>
        new(new HttpClient(), Microsoft.Extensions.Options.Options.Create(Options()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudinaryApiGateway>.Instance);

    private static CloudinaryMediaStorage Storage(
        ICloudinaryGateway gateway,
        CloudinaryOptions? options = null,
        ILogger? logger = null,
        RecordingDelay? delay = null,
        int retryAttempts = 3)
    {
        var resolved = options ?? Options();
        resolved.UploadRetryAttempts = retryAttempts;
        var policy = new CloudinaryRetryPolicy(
            resolved,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            delay ?? new RecordingDelay());

        return new CloudinaryMediaStorage(
            gateway,
            Microsoft.Extensions.Options.Options.Create(resolved),
            policy,
            (ILogger<CloudinaryMediaStorage>?)logger
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudinaryMediaStorage>.Instance);
    }

    /// <summary>Queued outcomes for one call, with the SDK-shaped request objects recorded.</summary>
    private sealed class FakeCloudinaryGateway : ICloudinaryGateway
    {
        private readonly Queue<CloudinaryAttemptResult> _responses = new();
        private Exception? _throwOnce;

        public FakeCloudinaryGateway(params CloudinaryAttemptResult[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        /// <summary>Makes the next SDK call fail the way a real transport fault would.</summary>
        public FakeCloudinaryGateway ThrowsOnce(Exception exception)
        {
            _throwOnce = exception;
            return this;
        }

        public bool Secure => true;

        public int UploadImageCallCount { get; private set; }

        public int UploadRawCallCount { get; private set; }

        public int DestroyCallCount { get; private set; }

        public SortedDictionary<string, object>? LastImageUploadParameters { get; private set; }

        public SortedDictionary<string, object>? LastRawUploadParameters { get; private set; }

        public DeletionParams? LastDeletionParameters { get; private set; }

        public List<string> FetchUrls { get; } = [];

        public Task<CloudinaryAttemptResult> UploadImageAsync(ImageUploadParams parameters, CancellationToken ct)
        {
            UploadImageCallCount++;
            LastImageUploadParameters = parameters.ToParamsDictionary();
            return Task.FromResult(Next(parameters.PublicId));
        }

        public Task<CloudinaryAttemptResult> UploadRawAsync(RawUploadParams parameters, CancellationToken ct)
        {
            UploadRawCallCount++;
            LastRawUploadParameters = parameters.ToParamsDictionary();
            // Cloudinary appends the original file's extension to a raw public id (verified live).
            return Task.FromResult(Next($"{parameters.PublicId}.pdf"));
        }

        public Task<CloudinaryAttemptResult> DestroyAsync(DeletionParams parameters, CancellationToken ct)
        {
            DestroyCallCount++;
            LastDeletionParameters = parameters;
            return Task.FromResult(Next(parameters.PublicId));
        }

        public Task<CloudinaryResourceInfo?> GetResourceAsync(
            string publicId, string resourceType, string deliveryType, CancellationToken ct) =>
            Task.FromResult<CloudinaryResourceInfo?>(new CloudinaryResourceInfo([], []));

        public string BuildDeliveryUrl(string publicId, string resourceType, string deliveryType, bool signed) =>
            $"https://res.cloudinary.com/a-cloud/{resourceType}/{deliveryType}/{(signed ? "s--test--/" : string.Empty)}{publicId}";

        public string BuildPrivateDownloadUrl(
            string publicId, string resourceType, string deliveryType, DateTimeOffset expiresAtUtc) =>
            $"https://api.cloudinary.com/v1_1/a-cloud/{resourceType}/download?public_id={publicId}&type={deliveryType}&expires_at={expiresAtUtc.ToUnixTimeSeconds()}";

        public Task<Stream> FetchAsync(string url, CancellationToken ct)
        {
            FetchUrls.Add(url);
            return Task.FromResult<Stream>(new MemoryStream([9, 8, 7], writable: false));
        }

        public static CloudinaryAttemptResult Success(
            string publicId = "aveline/test/catalog/test",
            string secureUrl = "https://res.cloudinary.com/a-cloud/image/upload/v1/aveline/test") =>
            new(200, null, publicId, secureUrl, secureUrl, 3, false);

        public static CloudinaryAttemptResult Failure(int statusCode, string? message = null) =>
            new(statusCode, message, null, null, null, 0, false);

        private CloudinaryAttemptResult Next(string? publicId)
        {
            if (_throwOnce is { } exception)
            {
                _throwOnce = null;
                throw exception;
            }

            return _responses.Count > 0 ? _responses.Dequeue() : Success(publicId ?? "aveline/test/catalog/test");
        }
    }

    private sealed class RecordingDelay : IMediaRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> AllMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            AllMessages.Add(message);
            if (exception is not null)
            {
                AllMessages.Add(exception.Message);
            }
        }
    }
}
