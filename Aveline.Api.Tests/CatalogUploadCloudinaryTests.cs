using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.2 (lane L2) — <see cref="CloudinaryInventoryImageStore"/>, the catalog row seam's
/// Cloudinary implementation (strategy §3.1, §3.3). It owns the <see cref="InventoryImage"/> row
/// and delegates the bytes to <see cref="IMediaStorage"/>; it must not implement the provider
/// seam, and it must not know a Cloudinary public-id format beyond the deterministic key
/// <see cref="MediaTagger.BuildCatalogPublicId"/> builds.
/// </summary>
/// <remarks>
/// <para>
/// The row shape is the strategy's final tier map (§3.3): <c>StorageProvider=cloudinary</c>, the
/// <c>StorageKey</c> the provider returned, an absolute secure CDN <c>ImageUrl</c> carrying the
/// one-width delivery contract, and <c>ImageData</c> written <b>only</b> when
/// <c>Media:DualWrite</c> is on.
/// </para>
/// <para>
/// Dual-write is pinned in <b>both</b> directions. The <c>false</c> case is the documented
/// default and a test that only checks the <c>true</c> case does not pin it.
/// </para>
/// </remarks>
public class CatalogUploadCloudinaryTests
{
    private static readonly Guid Org = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly byte[] Bytes = [1, 2, 3, 4, 5];

    private readonly RecordingMediaStorage _media = new();
    private readonly Mock<IInventoryRepository> _repository = new();
    private readonly List<InventoryImage> _added = [];

    public CatalogUploadCloudinaryTests()
    {
        _repository
            .Setup(r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()))
            .Callback<InventoryImage, CancellationToken>((image, _) => _added.Add(image))
            .Returns(Task.CompletedTask);
    }

    // ---------------------------------------------------------------------------------------
    // The seam is one seam
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Provider_IsTheCloudinaryTier()
    {
        Store().Provider.Should().Be("cloudinary");
    }

    [Fact]
    public void Store_ImplementsOnlyTheRowSeam()
    {
        // Strategy §3.1: no class implements two seams. The bytes are delegated, not implemented.
        typeof(CloudinaryInventoryImageStore).GetInterfaces()
            .Should().Equal(typeof(IInventoryImageStore));
    }

    // ---------------------------------------------------------------------------------------
    // Row shape (strategy §3.3's final tier map)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_WritesTheCloudinaryRowShape()
    {
        var image = await StoreAsync();

        image.StorageProvider.Should().Be("cloudinary");
        image.StorageKey.Should().Be($"image/upload:{MediaTagger.BuildCatalogPublicId(Org, image.Id)}");
        image.OrgId.Should().Be(Org);
        image.ContentType.Should().Be("image/jpeg");
        image.FileName.Should().Be("piece.jpg");
        image.FileSizeBytes.Should().Be(Bytes.LongLength);
        image.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task StoreAsync_PersistsTheRowThroughTheRepository()
    {
        var image = await StoreAsync();

        _added.Should().ContainSingle().Which.Should().BeSameAs(image);
        _repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreAsync_ImageUrlIsAnAbsoluteSecureCdnUrl()
    {
        var image = await StoreAsync();

        Uri.TryCreate(image.ImageUrl, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
        uri.Host.Should().Be("res.cloudinary.com");
        image.ImageUrl.Should().Contain(image.Id.ToString(), "the deterministic public id names the row");
    }

    [Fact]
    public async Task StoreAsync_ImageUrlCarriesTheOneWidthDeliveryContract()
    {
        var image = await StoreAsync(width: 800);

        image.ImageUrl.Should().Contain("w_800,f_auto,q_auto");
        image.ImageUrl.Should().NotContain("f_webp");
        image.ImageUrl.Should().NotContain("q_85");
        System.Text.RegularExpressions.Regex.Matches(image.ImageUrl, "w_").Should().HaveCount(1);
    }

    [Fact]
    public async Task StoreAsync_UsesTheConfiguredDisplayWidth()
    {
        var image = await StoreAsync(width: 640);

        image.ImageUrl.Should().Contain("w_640,f_auto,q_auto");
        System.Text.RegularExpressions.Regex.Matches(image.ImageUrl, "w_").Should().HaveCount(1);
    }

    [Fact]
    public async Task StoreAsync_CarriesTheItemIdWhenThereIsOne()
    {
        var itemId = Guid.NewGuid();

        var image = await StoreAsync(itemId: itemId);

        image.ItemId.Should().Be(itemId);
    }

    [Fact]
    public void TheCatalogStorageKey_FitsThePersistedColumn()
    {
        // Strategy §3.3: the encoded key must fit InventoryImages.StorageKey varchar(200). The
        // components are GUIDs, so the rendered length is a fixed worst case rather than a guess.
        var key = $"image/upload:{MediaTagger.BuildCatalogPublicId(Guid.NewGuid(), Guid.NewGuid())}";

        key.Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task StoreAsync_ConsumesTheStorageKeyTheProviderReturned()
    {
        // The provider's returned key is authoritative: Cloudinary appends an extension to a raw
        // public id (unit U1.1's live finding), so re-deriving it would name an asset the delete
        // and fetch paths cannot resolve.
        _media.StorageKeyFactory = request => $"image/upload:{request.PublicId}-provider-returned";

        var image = await StoreAsync();

        image.StorageKey.Should().Be(
            $"image/upload:{MediaTagger.BuildCatalogPublicId(Org, image.Id)}-provider-returned");
    }

    // ---------------------------------------------------------------------------------------
    // Bytes and metadata go through the provider seam
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_DelegatesTheBytesWithTheCatalogMetadata()
    {
        var image = await StoreAsync();

        var request = _media.LastRequest!;
        request.PublicId.Should().Be(MediaTagger.BuildCatalogPublicId(Org, image.Id));
        request.Bytes.Should().Equal(Bytes);
        request.ContentType.Should().Be("image/jpeg");
        request.FileName.Should().Be("piece.jpg");
        request.Tier.Should().Be(MediaTier.Public, "the catalog tier is always the public delivery type");

        request.Metadata.Should().NotBeNull();
        request.Metadata!.Labels.Should().Contain("catalog-image")
            .And.Contain("kind:image")
            .And.Contain($"organizationId:{Org}");
        request.Metadata.Attributes["o"].Should().Be(Org.ToString());
        request.Metadata.Attributes["s"].Should().Be("catalog");
        request.Metadata.Attributes.Should().NotContainKey("fileName");
        request.Metadata.Attributes.Should().NotContainKey("contentType");
    }

    [Fact]
    public async Task StoreAsync_WhenTheProviderRefuses_WritesNoRow()
    {
        _media.FailOnPut = true;

        var act = () => StoreAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _added.Should().BeEmpty("a row that names an asset the provider refused would dangle");
    }

    // ---------------------------------------------------------------------------------------
    // Dual-write, pinned in both directions (strategy §3.3; migration plan §6.4 step 6)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DualWrite_DefaultsToOff()
    {
        new MediaOptions().DualWrite.Should().BeFalse(
            "keeping every byte in the database defeats the point of the migration (strategy §0.1)");
    }

    [Fact]
    public async Task StoreAsync_WithDualWriteOff_LeavesTheRowBytesNull()
    {
        var image = await StoreAsync(dualWrite: false);

        image.ImageData.Should().BeNull();
        _added.Should().ContainSingle().Which.ImageData.Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_WithDualWriteOn_WritesTheSecondCopyOnTheSameRow()
    {
        var image = await StoreAsync(dualWrite: true);

        image.ImageData.Should().Equal(Bytes);
        _added.Should().ContainSingle().Which.ImageData.Should().Equal(Bytes);
        // The provider still received its own untouched copy (the seam does not consume the array).
        _media.LastRequest!.Bytes.Should().Equal(Bytes);
    }

    // ---------------------------------------------------------------------------------------
    // Open and delete
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenReadAsync_ReturnsNullWhenTheBytesLiveAtTheCdn()
    {
        var image = await StoreAsync(dualWrite: false);

        (await Store().OpenReadAsync(image)).Should().BeNull();
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsTheDualWriteCopyAsANonWritableStream()
    {
        var image = await StoreAsync(dualWrite: true);

        using var stream = await Store().OpenReadAsync(image);

        stream.Should().NotBeNull();
        stream!.CanWrite.Should().BeFalse();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(Bytes);
    }

    [Fact]
    public async Task DeleteAsync_TellsTheProviderWithTheRowsOwnKey()
    {
        var image = await StoreAsync();

        await Store().DeleteAsync(image);

        _media.Deleted.Should().ContainSingle();
        _media.Deleted[0].Provider.Should().Be("cloudinary");
        _media.Deleted[0].StorageKey.Should().Be(image.StorageKey);
        _media.Deleted[0].ContentType.Should().Be("image/jpeg");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private CloudinaryInventoryImageStore Store(bool dualWrite = false, int width = 800) =>
        new(
            _media,
            _repository.Object,
            Options.Create(new MediaOptions { DualWrite = dualWrite, CatalogDisplayWidth = width }));

    private Task<InventoryImage> StoreAsync(bool dualWrite = false, Guid? itemId = null, int width = 800) =>
        Store(dualWrite, width).StoreAsync(new InventoryImageStoreRequest(
            Org, itemId, Bytes, "image/jpeg", "piece.jpg", Bytes.LongLength));

    /// <summary>
    /// The provider seam, recorded rather than mocked, so the upload request and the returned
    /// storage key are observable. Implements the seam once, in the fake — the store under test
    /// does not.
    /// </summary>
    private sealed class RecordingMediaStorage : IMediaStorage
    {
        public string Provider => "cloudinary";

        public MediaPutRequest? LastRequest { get; private set; }

        public List<StoredMediaRef> Deleted { get; } = [];

        public bool FailOnPut { get; set; }

        public Func<MediaPutRequest, string>? StorageKeyFactory { get; set; }

        public Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (FailOnPut)
            {
                throw new InvalidOperationException("the provider refused the upload");
            }

            var storageKey = StorageKeyFactory?.Invoke(request) ?? $"image/upload:{request.PublicId}";
            var publicId = storageKey[(storageKey.IndexOf(':') + 1)..];
            var url = $"https://res.cloudinary.com/a-cloud/image/upload/v1712345678/{publicId}.jpg";

            return Task.FromResult(new StoredMedia(Provider, storageKey, url));
        }

        public Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default)
        {
            Deleted.Add(reference);
            return Task.CompletedTask;
        }

        public string PublicDeliveryUrl(StoredMediaRef reference) => reference.StorageKey;

        public string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc) =>
            reference.StorageKey;
    }
}

/// <summary>
/// U1.2 (lane L2) — the module registers its own row seam and the implementation follows
/// <c>Media:Provider</c> (strategy §3.1): a cloudinary-configured host resolves the Cloudinary
/// row seam, the default host resolves the database one, and no caller branches on the provider.
/// </summary>
public class CatalogRowSeamSelectionTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _cloudinaryFactory = null!;
    private WebApplicationFactory<Program> _defaultFactory = null!;

    public Task InitializeAsync()
    {
        // The cloudinary host needs a complete, valid configuration because
        // MediaOptionsValidator refuses a half-configured provider at startup (strategy §3.4).
        // The byte provider is replaced, so nothing reaches the network.
        _cloudinaryFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Media:Provider", "cloudinary");
                builder.UseSetting("Media:SigningKey", Convert.ToBase64String(new byte[32]));
                builder.UseSetting("Media:PublicBaseUrl", "https://api.example.test");
                builder.UseSetting("CLOUDINARY_URL", "cloudinary://test-key:test-secret@a-cloud");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMediaStorage>();
                    services.AddSingleton(Mock.Of<IMediaStorage>());
                });
            });

        _defaultFactory = new WebApplicationFactory<Program>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _cloudinaryFactory.DisposeAsync();
        await _defaultFactory.DisposeAsync();
    }

    [Fact]
    public void WithProviderCloudinary_ResolvesTheCloudinaryRowSeam()
    {
        using var scope = _cloudinaryFactory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IInventoryImageStore>()
            .Should().BeOfType<CloudinaryInventoryImageStore>();
    }

    [Fact]
    public void WithTheDefaultProvider_ResolvesTheDatabaseRowSeam()
    {
        using var scope = _defaultFactory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IInventoryImageStore>()
            .Should().BeOfType<DatabaseInventoryImageStore>();
    }
}
