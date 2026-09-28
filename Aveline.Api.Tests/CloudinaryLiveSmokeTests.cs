using System.Text;
using Aveline.Api.Modules.Media;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.1's opt-in live suite (strategy §5.1 S1, R17's mitigation): the four SDK operations against
/// the real Cloudinary account — upload, destroy, a <c>Signed</c> delivery URL, and the
/// <c>DownloadPrivate</c> fetch path — plus the <c>Overwrite=false</c> case that must keep an
/// existing asset's tags (risk R5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Trait-gated and inert by default.</b> The class carries
/// <c>[Trait("Category","CloudinaryLive")]</c>, and every test returns early with a printed
/// <c>SKIPPED</c> reason when no credential is configured, so a normal full-suite run makes no
/// network call and stays green. Run it deliberately with
/// <c>dotnet test … --filter "Category=CloudinaryLive"</c> after exporting <c>CLOUDINARY_URL</c>
/// (or the discrete <c>CLOUDINARY_API_KEY</c>/<c>CLOUDINARY_API_SECRET</c>).
/// </para>
/// <para>
/// The credential is read the way the application reads it — from the environment through
/// <see cref="CloudinaryUrlParser"/> — never hard-coded and never printed. The account secret
/// never reaches the test output: only status codes, byte counts and content types are written.
/// </para>
/// </remarks>
[Trait("Category", "CloudinaryLive")]
public class CloudinaryLiveSmokeTests
{
    private readonly ITestOutputHelper _output;

    public CloudinaryLiveSmokeTests(ITestOutputHelper output) => _output = output;

    // ---------------------------------------------------------------------------------------
    // 1. Upload, then destroy
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_ThenDestroy_RoundTrips()
    {
        if (!LiveSession.TryCreate(out var live, out var reason))
        {
            _output.WriteLine($"SKIPPED: {reason}");
            return;
        }

        using var _ = live;
        var orgId = Guid.NewGuid();
        var publicId = MediaTagger.BuildCatalogPublicId(orgId, Guid.NewGuid());

        var stored = await live.Storage.PutAsync(new MediaPutRequest(
            publicId, SyntheticImage.TinyJpeg(), "image/jpeg", "smoke.jpg", MediaTier.Public,
            MediaTagger.ForCatalogImage(orgId, DateTimeOffset.UtcNow)));

        try
        {
            _output.WriteLine($"upload: provider={stored.Provider} url={Sanitise(stored.Url)} bytes={SyntheticImage.TinyJpeg().Length}");
            stored.Provider.Should().Be("cloudinary");
            stored.StorageKey.Should().Be($"image/upload:{publicId}");
            stored.Url.Should().StartWith("https://res.cloudinary.com/");
        }
        finally
        {
            await live.Storage.DeleteAsync(StoredRef(stored.StorageKey, "image/jpeg"));
        }

        var afterDelete = await live.Gateway.GetResourceAsync(publicId, "image", "upload", CancellationToken.None);
        afterDelete.Should().BeNull("destroy must remove the asset");
        _output.WriteLine("destroy: asset is gone from the Admin API");
    }

    // ---------------------------------------------------------------------------------------
    // 2. Signed delivery (`Signed`)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SignedDeliveryUrl_IsSignedAndActuallyFetchable()
    {
        if (!LiveSession.TryCreate(out var live, out var reason))
        {
            _output.WriteLine($"SKIPPED: {reason}");
            return;
        }

        using var _ = live;
        var orgId = Guid.NewGuid();
        var publicId = MediaTagger.BuildCatalogPublicId(orgId, Guid.NewGuid());

        var stored = await live.Storage.PutAsync(new MediaPutRequest(
            publicId, SyntheticImage.TinyJpeg(), "image/jpeg", "signed.jpg", MediaTier.Public,
            MediaTagger.ForCatalogImage(orgId, DateTimeOffset.UtcNow)));

        try
        {
            var signed = live.Storage.SignedDeliveryUrl(
                StoredRef(stored.StorageKey, "image/jpeg"), DateTimeOffset.UtcNow.AddMinutes(5));

            signed.Should().StartWith("https://");
            signed.Should().Contain("/image/upload/s--");
            _output.WriteLine($"signed url: {Sanitise(signed)}");

            var response = await live.Http.GetAsync(signed);
            _output.WriteLine($"signed fetch: {(int)response.StatusCode} {response.Content.Headers.ContentType}");
            response.IsSuccessStatusCode.Should().BeTrue();
        }
        finally
        {
            await live.Storage.DeleteAsync(StoredRef(stored.StorageKey, "image/jpeg"));
        }
    }

    // ---------------------------------------------------------------------------------------
    // 3. The private fetch path (`DownloadPrivate`) and the raw PDF round trip
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task PrivateDownload_ReturnsTheStoredPdfBytes()
    {
        if (!LiveSession.TryCreate(out var live, out var reason))
        {
            _output.WriteLine($"SKIPPED: {reason}");
            return;
        }

        using var _ = live;
        var orgId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var publicId = MediaTagger.BuildConversationPublicId(orgId, Guid.NewGuid());
        var pdf = SyntheticImage.TinyPdf();

        var stored = await live.Storage.PutAsync(new MediaPutRequest(
            publicId, pdf, "application/pdf", "smoke.pdf", MediaTier.Protected,
            MediaTagger.ForSalonAttachment(
                MediaSource.Web, orgId, conversationId, Guid.NewGuid(), null, "application/pdf",
                DateTimeOffset.UtcNow)));

        try
        {
            // Cloudinary appends the raw file's extension to the requested public id; the stored
            // key carries the provider's actual id so delete and fetch resolve.
            stored.StorageKey.Should().StartWith("raw/authenticated:");
            stored.StorageKey.Should().EndWith(".pdf");
            _output.WriteLine($"raw key: {Sanitise(stored.StorageKey)}");

            var reference = StoredRef(stored.StorageKey, "application/pdf");
            var signed = live.Storage.SignedDeliveryUrl(reference, DateTimeOffset.UtcNow.AddMinutes(10));
            signed.Should().StartWith("https://api.cloudinary.com/");
            signed.Should().Contain("expires_at=");

            var response = await live.Http.GetAsync(signed);
            var downloaded = await response.Content.ReadAsByteArrayAsync();
            _output.WriteLine(
                $"private download: {(int)response.StatusCode} {response.Content.Headers.ContentType} bytes={downloaded.Length}");
            response.IsSuccessStatusCode.Should().BeTrue();
            downloaded.Should().Equal(pdf);

            var streamed = await live.Storage.OpenReadAsync(reference);
            streamed.Should().NotBeNull();
            using var buffer = new MemoryStream();
            await streamed!.CopyToAsync(buffer);
            _output.WriteLine($"OpenReadAsync: bytes={buffer.Length}");
            buffer.ToArray().Should().Equal(pdf);
        }
        finally
        {
            await live.Storage.DeleteAsync(StoredRef(stored.StorageKey, "application/pdf"));
        }
    }

    // ---------------------------------------------------------------------------------------
    // 4. Overwrite=false means an existing asset keeps its tags (risk R5)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ReUploadWithOverwriteFalse_KeepsTheExistingTags()
    {
        if (!LiveSession.TryCreate(out var live, out var reason))
        {
            _output.WriteLine($"SKIPPED: {reason}");
            return;
        }

        using var _ = live;
        var orgId = Guid.NewGuid();
        var publicId = MediaTagger.BuildCatalogPublicId(orgId, Guid.NewGuid());

        var first = await live.Storage.PutAsync(new MediaPutRequest(
            publicId, SyntheticImage.TinyJpeg(), "image/jpeg", "first.jpg", MediaTier.Public,
            new MediaMetadata(["live-tag-first"], new Dictionary<string, string> { ["o"] = orgId.ToString() })));

        try
        {
            var before = await live.Gateway.GetResourceAsync(publicId, "image", "upload", CancellationToken.None);
            before.Should().NotBeNull();
            before!.Tags.Should().Contain("live-tag-first");
            _output.WriteLine($"first upload tags: {string.Join(',', before.Tags)}");

            // A second upload to the same deterministic public id: 200-with-`existing`, no writes.
            var second = await live.Storage.PutAsync(new MediaPutRequest(
                publicId, SyntheticImage.TinyJpeg(), "image/jpeg", "second.jpg", MediaTier.Public,
                new MediaMetadata(["live-tag-second"], new Dictionary<string, string> { ["o"] = orgId.ToString() })));

            second.StorageKey.Should().Be(first.StorageKey);

            var after = await live.Gateway.GetResourceAsync(publicId, "image", "upload", CancellationToken.None);
            after.Should().NotBeNull();
            _output.WriteLine($"after re-upload tags: {string.Join(',', after!.Tags)}");
            after.Tags.Should().Contain("live-tag-first", "overwrite=false must not clear the tags (risk R5)");
            after.Tags.Should().NotContain("live-tag-second", "overwrite=false must not merge new tags either");
        }
        finally
        {
            await live.Storage.DeleteAsync(StoredRef(first.StorageKey, "image/jpeg"));
        }
    }

    // ---------------------------------------------------------------------------------------
    // 5. The two numbers S1 owes (§3.7, §R8): delivered size and real derivation count
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task CatalogDelivery_ReportsDeliveredSizeAndDerivationCount()
    {
        if (!LiveSession.TryCreate(out var live, out var reason))
        {
            _output.WriteLine($"SKIPPED: {reason}");
            return;
        }

        using var _ = live;
        var orgId = Guid.NewGuid();
        var bmpId = MediaTagger.BuildCatalogPublicId(orgId, Guid.NewGuid());
        var bmp = SyntheticImage.NoisyBmp(width: 1280, height: 960);

        // A browser/phone upload is a JPEG, so the measurement is taken on a JPEG source: the
        // synthetic source is delivered once as `w_1280,f_jpg,q_85` and that result is uploaded.
        var sourceAsset = await live.Storage.PutAsync(new MediaPutRequest(
            bmpId, bmp, "image/bmp", "source.bmp", MediaTier.Public,
            MediaTagger.ForCatalogImage(orgId, DateTimeOffset.UtcNow)));

        StoredMedia? measured = null;
        try
        {
            var jpegSourceUrl = sourceAsset.Url.Replace(
                "/upload/", "/upload/w_1280,f_jpg,q_85/", StringComparison.Ordinal);
            var jpegSource = (await MeasureAsync(live.Http, jpegSourceUrl, null)).Data;
            jpegSource.Length.Should().BeGreaterThan(0);

            var jpegId = MediaTagger.BuildCatalogPublicId(orgId, Guid.NewGuid());
            measured = await live.Storage.PutAsync(new MediaPutRequest(
                jpegId, jpegSource, "image/jpeg", "measure.jpg", MediaTier.Public,
                MediaTagger.ForCatalogImage(orgId, DateTimeOffset.UtcNow)));

            // The display variant is U1.2's builder; this only measures what one width plus f_auto
            // actually delivers, so it inserts the transformation into the delivery URL directly.
            var transformed = measured.Url.Replace(
                "/upload/", "/upload/w_800,f_auto,q_auto/", StringComparison.Ordinal);

            var original = await MeasureAsync(live.Http, measured.Url, null);
            var webpOnly = await MeasureAsync(live.Http, transformed, "image/webp");
            var clientHeader = await MeasureAsync(live.Http, transformed, "image/webp,image/jpeg,*/*");
            var jpegOnly = await MeasureAsync(live.Http, transformed, "image/jpeg");

            var resource = await live.Gateway.GetResourceAsync(jpegId, "image", "upload", CancellationToken.None);

            _output.WriteLine($"source: {jpegSource.Length} bytes (image/jpeg 1280px, delivered as w_1280,f_jpg,q_85)");
            _output.WriteLine($"delivered original: {original.Data.Length} bytes ({original.ContentType})");
            _output.WriteLine($"delivered w_800,f_auto,q_auto [Accept image/webp]: {webpOnly.Data.Length} bytes ({webpOnly.ContentType})");
            _output.WriteLine($"delivered w_800,f_auto,q_auto [Accept image/webp,image/jpeg,*/*]: {clientHeader.Data.Length} bytes ({clientHeader.ContentType})");
            _output.WriteLine($"delivered w_800,f_auto,q_auto [Accept image/jpeg]: {jpegOnly.Data.Length} bytes ({jpegOnly.ContentType})");
            _output.WriteLine($"derivations reported by the Admin API: {resource?.DerivedFormats.Count ?? 0}"
                + (resource is null ? string.Empty : $" [{string.Join(',', resource.DerivedFormats)}]"));

            clientHeader.Data.Length.Should().BeLessThan(original.Data.Length);
            (resource?.DerivedFormats.Count ?? 0).Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await live.Storage.DeleteAsync(StoredRef(sourceAsset.StorageKey, "image/bmp"));
            if (measured is not null)
            {
                await live.Storage.DeleteAsync(StoredRef(measured.StorageKey, "image/jpeg"));
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static StoredMediaRef StoredRef(string storageKey, string contentType) =>
        new("cloudinary", storageKey, contentType);

    private static async Task<(byte[] Data, string ContentType)> MeasureAsync(
        HttpClient http, string url, string? accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(accept))
        {
            // A comma-separated Accept list is a valid HTTP header but not a valid single media type.
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        using var response = await http.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return (bytes, response.Content.Headers.ContentType?.MediaType ?? "unknown");
    }

    /// <summary>Strips any query string, so a signed URL's signature never lands in the output.</summary>
    private static string Sanitise(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : url;

    /// <summary>
    /// One configured connection to the real account. <see cref="TryCreate"/> refuses — without
    /// touching the network — when no credential is present, which is what keeps the class inert.
    /// </summary>
    private sealed class LiveSession : IDisposable
    {
        private LiveSession(CloudinaryMediaStorage storage, CloudinaryApiGateway gateway, HttpClient http)
        {
            Storage = storage;
            Gateway = gateway;
            Http = http;
        }

        public CloudinaryMediaStorage Storage { get; }

        public CloudinaryApiGateway Gateway { get; }

        public HttpClient Http { get; }

        public static bool TryCreate(out LiveSession? session, out string reason)
        {
            session = null;

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            if (!CloudinaryUrlParser.TryResolve(configuration, out var credentials, out var error)
                || string.IsNullOrWhiteSpace(credentials.CloudName)
                || string.IsNullOrWhiteSpace(credentials.ApiKey)
                || string.IsNullOrWhiteSpace(credentials.ApiSecret))
            {
                reason = "no Cloudinary credential is configured "
                    + "(set CLOUDINARY_URL, or CLOUDINARY_API_KEY + CLOUDINARY_API_SECRET + CLOUDINARY_CLOUD_NAME). "
                    + (error ?? string.Empty);
                return false;
            }

            var options = new CloudinaryOptions
            {
                CloudName = credentials.CloudName,
                ApiKey = credentials.ApiKey,
                ApiSecret = credentials.ApiSecret,
                CatalogDeliveryType = "upload",
                ProtectedDeliveryType = "authenticated",
                UploadRetryAttempts = 3,
            };

            var optionsAccessor = Options.Create(options);
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var gateway = new CloudinaryApiGateway(
                http, optionsAccessor, NullLogger<CloudinaryApiGateway>.Instance);
            var retry = new CloudinaryRetryPolicy(options, NullLogger.Instance);
            var storage = new CloudinaryMediaStorage(
                gateway, optionsAccessor, retry, NullLogger<CloudinaryMediaStorage>.Instance);

            reason = string.Empty;
            session = new LiveSession(storage, gateway, http);
            return true;
        }

        public void Dispose() => Http.Dispose();
    }
}

/// <summary>The smallest bytes that make each live case real: a JPEG and a PDF, plus a BMP for
/// the delivery-size measurement.</summary>
internal static class SyntheticImage
{
    /// <summary>A 1x1 JPEG, the smallest valid image the upload API accepts.</summary>
    public static byte[] TinyJpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a"
        + "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA"
        + "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==");

    /// <summary>A structurally minimal PDF; the raw resource type stores bytes as-is.</summary>
    public static byte[] TinyPdf() => Encoding.ASCII.GetBytes(
        "%PDF-1.4\n"
        + "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n"
        + "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
        + "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n"
        + "trailer<</Root 1 0 R/Size 4>>\n"
        + "%%EOF\n");

    /// <summary>
    /// A deterministic 24-bit BMP with a gradient and pseudo-noise, so the delivered WebP/JPEG is
    /// a realistic size rather than a one-pixel toy. No image library is referenced, so the bytes
    /// are assembled here.
    /// </summary>
    public static byte[] NoisyBmp(int width, int height)
    {
        var rowSize = ((width * 3 + 3) / 4) * 4;
        var pixels = rowSize * height;
        var bmp = new byte[54 + pixels];

        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        BitConverter.GetBytes(pixels).CopyTo(bmp, 34);
        BitConverter.GetBytes(2835).CopyTo(bmp, 38);
        BitConverter.GetBytes(2835).CopyTo(bmp, 42);

        var seed = 123456789u;
        for (var y = 0; y < height; y++)
        {
            // BMP rows are bottom-up.
            var row = 54 + ((height - 1 - y) * rowSize);
            for (var x = 0; x < width; x++)
            {
                seed = (seed * 1664525u) + 1013904223u;
                var noise = (byte)(seed >> 24);
                var offset = row + (x * 3);
                bmp[offset] = (byte)((noise + 128) & 0xFF);
                bmp[offset + 1] = (byte)(((y * 255 / height) + noise) / 2);
                bmp[offset + 2] = (byte)(((x * 255 / width) + noise) / 2);
            }
        }

        return bmp;
    }
}
