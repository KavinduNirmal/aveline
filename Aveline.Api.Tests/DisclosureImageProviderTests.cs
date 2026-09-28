using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Privacy.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// The provider decides the URL Meta will fetch, so these cover the three things that would break a
/// disclosure in production: an id that does not change when the boutique renames, a URL that is not
/// publicly fetchable, and a missing Cloudinary client taking the host down instead of degrading to
/// text.
/// </summary>
public class DisclosureImageProviderTests
{
    private static readonly IDisclosureImageComposer Composer = new DisclosureImageComposer();

    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static DisclosureImageProvider NewProvider(
        ICloudinaryGateway? gateway = null, string cloudName = "")
        => new(
            Composer,
            NewCache(),
            Options.Create(new CloudinaryOptions { CloudName = cloudName }),
            NullLogger<DisclosureImageProvider>.Instance,
            gateway);

    [Fact]
    public async Task GetUrl_ReturnsNull_WhenNoCloudinaryClientIsConfigured()
    {
        // The point of the null return: a host without the Cloudinary gateway (a test host, or a
        // deployment on the database media provider) must degrade the disclosure to text rather than
        // fail to start. This exact mistake previously failed every integration test at host build.
        var url = await NewProvider(gateway: null, cloudName: "a-cloud").GetUrlAsync(Guid.NewGuid(), "Aurora");

        Assert.Null(url);
    }

    [Fact]
    public async Task GetUrl_ReturnsNull_WhenTheCloudNameIsMissing()
    {
        var url = await NewProvider(gateway: new RecordingGateway(), cloudName: "")
            .GetUrlAsync(Guid.NewGuid(), "Aurora");

        Assert.Null(url);
    }

    [Fact]
    public void BuildPublicId_IsDeterministic()
    {
        var org = Guid.Parse("01a0d6bb-71f5-7e97-a3e2-e8aec2eb4f4f");

        Assert.Equal(
            DisclosureImageProvider.BuildPublicId(org, "Aurora Boutique", "v1"),
            DisclosureImageProvider.BuildPublicId(org, "Aurora Boutique", "v1"));
    }

    [Fact]
    public void BuildPublicId_ChangesWhenTheBoutiqueRenames()
    {
        var org = Guid.Parse("01a0d6bb-71f5-7e97-a3e2-e8aec2eb4f4f");

        // Cloudinary delivery is immutable per public id, so a rename that kept the id would keep
        // showing the old name on new disclosures.
        Assert.NotEqual(
            DisclosureImageProvider.BuildPublicId(org, "Aurora Boutique", "v1"),
            DisclosureImageProvider.BuildPublicId(org, "Aurora Boutique Colombo", "v1"));
    }

    [Fact]
    public void BuildPublicId_ChangesWithTheLayoutVersion()
    {
        var org = Guid.Parse("01a0d6bb-71f5-7e97-a3e2-e8aec2eb4f4f");

        Assert.NotEqual(
            DisclosureImageProvider.BuildPublicId(org, "Aurora Boutique", "v1"),
            DisclosureImageProvider.BuildPublicId(org, "Aurora Boutique", "v2"));
    }

    [Fact]
    public void BuildPublicId_IsScopedToTheOrganization()
    {
        var first = DisclosureImageProvider.BuildPublicId(Guid.NewGuid(), "Aurora Boutique", "v1");
        var second = DisclosureImageProvider.BuildPublicId(Guid.NewGuid(), "Aurora Boutique", "v1");

        Assert.NotEqual(first, second);
        Assert.StartsWith("aveline/disclosure/", first, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDeliveryUrl_IsUnsignedAndVersionless()
    {
        var url = DisclosureImageProvider.BuildDeliveryUrl("a-cloud", "aveline/disclosure/abc-123");

        // Version-less matters: an uploaded asset's version is not knowable before the upload, and a
        // URL carrying one would have to be stored. Unsigned matters more: Meta fetches this with no
        // credential of ours, so a signed URL would work now and 403 later.
        Assert.Equal(
            "https://res.cloudinary.com/a-cloud/image/upload/aveline/disclosure/abc-123.jpg", url);
        Assert.DoesNotContain("signature", url, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A gateway that never talks to Cloudinary. The upload path itself is exercised by the live
    /// smoke suite; what matters here is that the provider's answers do not depend on the network.
    /// </summary>
    private sealed class RecordingGateway : ICloudinaryGateway
    {
        public bool Secure => true;

        public int Uploads { get; private set; }

        public Task<CloudinaryAttemptResult> UploadImageAsync(
            CloudinaryDotNet.Actions.ImageUploadParams parameters, CancellationToken ct)
        {
            Uploads++;
            return Task.FromResult(new CloudinaryAttemptResult(
                StatusCode: 200, ErrorMessage: null, PublicId: parameters.PublicId,
                SecureUrl: "https://example.invalid/image.jpg", Url: null, Bytes: 1, Existing: false));
        }

        public Task<CloudinaryAttemptResult> UploadRawAsync(
            CloudinaryDotNet.Actions.RawUploadParams parameters, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<CloudinaryAttemptResult> DestroyAsync(
            CloudinaryDotNet.Actions.DeletionParams parameters, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<CloudinaryResourceInfo?> GetResourceAsync(
            string publicId, string resourceType, string deliveryType, CancellationToken ct)
            => Task.FromResult<CloudinaryResourceInfo?>(null);

        // Present on the real gateway, which builds signed and unsigned delivery URLs through the SDK.
        // The provider deliberately builds its own instead: a version-less, unsigned URL can be
        // derived before the upload and therefore does not have to be stored anywhere, which is the
        // property that removes a second source of truth.
        public string BuildDeliveryUrl(
            string publicId, string resourceType, string deliveryType, bool signed)
            => DisclosureImageProvider.BuildDeliveryUrl("a-cloud", publicId);

        public string BuildPrivateDownloadUrl(
            string publicId, string resourceType, string deliveryType, DateTimeOffset expiresAtUtc)
            => throw new NotSupportedException();

        public Task<Stream> FetchAsync(string url, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
