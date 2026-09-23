using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Media;
using CloudinaryDotNet.Actions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.3 (strategy §5.1 S2) — the conversation <em>row</em> seam's Cloudinary implementation. It
/// implements <see cref="IAttachmentStore"/> only, and delegates every byte to
/// <see cref="IMediaStorage"/>; no class implements two seams (strategy §3.1).
/// </summary>
/// <remarks>
/// The assertions are on what the provider seam was actually handed: the seven tags and the
/// <c>context</c> record from strategy §3.2, the fixed authenticated Aveline route on
/// <see cref="MessageAttachment.Url"/>, and the dual-write second copy on the row. The PDF and
/// image arms run through the real <see cref="CloudinaryMediaStorage"/> so the delivery type is
/// proven at the seam boundary, not merely asserted on the request object.
/// </remarks>
public class CloudinaryAttachmentStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 30, 0, TimeSpan.Zero);

    private const string ExpectedDate = "2026-09-21";

    private readonly AppDbContext _context;

    public CloudinaryAttachmentStoreTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CloudinaryAttachmentStore_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    // ---------------------------------------------------------------------------------------
    // The seam is one seam
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ImplementsOnlyTheRowSeam()
    {
        typeof(CloudinaryAttachmentStore).GetInterfaces().Should().Equal(typeof(IAttachmentStore));
    }

    // ---------------------------------------------------------------------------------------
    // Tags and context (strategy §3.2; salon §6.2/§6.3)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_StaffUpload_StoresTheSevenTagsAndTheContextRecord()
    {
        var storage = new RecordingMediaStorage();
        var store = Store(storage);
        var organizationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var attachment = await store.StoreAsync(Request(
            organizationId, conversationId, userId, MediaSource.Web));

        var metadata = storage.LastPut!.Metadata!;
        metadata.Labels.Should().Equal(
            "salon-image",
            "source:web",
            "kind:image",
            $"conversationId:{conversationId}",
            $"organizationId:{organizationId}",
            $"userId:{userId}",
            $"date:{ExpectedDate}");

        var context = metadata.Attributes;
        context["o"].Should().Be(organizationId.ToString());
        context["v"].Should().Be(conversationId.ToString());
        context["u"].Should().Be(userId.ToString());
        context["d"].Should().Be(ExpectedDate);
        context["s"].Should().Be("web");
        context.Should().NotContainKey("c");
        context.Should().NotContainKey("attachmentId");
    }

    [Fact]
    public async Task StoreAsync_InboundUpload_RendersUserIdNoneAndSourceWhatsapp()
    {
        var storage = new RecordingMediaStorage();
        var store = Store(storage);
        var organizationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await store.StoreAsync(Request(
            organizationId, conversationId, uploadedByUserId: null, MediaSource.WhatsApp,
            customerId: customerId));

        var metadata = storage.LastPut!.Metadata!;
        metadata.Labels.Should().Contain("userId:none").And.Contain("source:whatsapp");
        metadata.Labels.Should().NotContain(label => label.StartsWith("userId:", StringComparison.Ordinal)
            && label != "userId:none");
        metadata.Attributes["u"].Should().Be("none");
        metadata.Attributes["s"].Should().Be("whatsapp");
        // The real actor on the inbound path is the customer, not a substituted user id (salon §6.5).
        metadata.Attributes["c"].Should().Be(customerId.ToString());
    }

    [Fact]
    public async Task StoreAsync_UsesTheDeterministicConversationPublicId()
    {
        var storage = new RecordingMediaStorage();
        var store = Store(storage);
        var organizationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var attachment = await store.StoreAsync(Request(
            organizationId, conversationId, Guid.NewGuid(), MediaSource.Web));

        storage.LastPut!.PublicId.Should()
            .Be(MediaTagger.BuildConversationPublicId(organizationId, attachment.Id));
        storage.LastPut.Tier.Should().Be(MediaTier.Protected);
    }

    // ---------------------------------------------------------------------------------------
    // Dual-write: the row seam writes the second copy (strategy §3.3, migration plan §6.4 step 6)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_WithDualWrite_WritesTheBytesOnTheRow()
    {
        var bytes = new byte[] { 10, 20, 30, 40 };
        var store = Store(new RecordingMediaStorage(), dualWrite: true);

        var attachment = await store.StoreAsync(Request(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MediaSource.Web, bytes: bytes));

        attachment.ImageData.Should().Equal(bytes);
        var row = await _context.MessageAttachments.SingleAsync(a => a.Id == attachment.Id);
        row.ImageData.Should().Equal(bytes);
    }

    [Fact]
    public async Task StoreAsync_ByDefault_LeavesImageDataNull()
    {
        // The default is the point of the migration: the database must not stay the asset store
        // (strategy §0.1). Nothing writes the bytes unless DualWrite says so.
        var store = Store(new RecordingMediaStorage(), dualWrite: false);

        var attachment = await store.StoreAsync(Request(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MediaSource.Web));

        attachment.ImageData.Should().BeNull();
        var row = await _context.MessageAttachments.SingleAsync(a => a.Id == attachment.Id);
        row.ImageData.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // The delivery type comes from the content type (strategy §3.2/§3.3)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_Pdf_IsARawAuthenticatedUploadTaggedKindPdf()
    {
        var gateway = new FakeCloudinaryGateway();
        var store = Store(CloudinaryStorage(gateway));
        var organizationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var attachment = await store.StoreAsync(Request(
            organizationId, conversationId, Guid.NewGuid(), MediaSource.Web, contentType: "application/pdf"));

        gateway.LastImageUploadParameters.Should().BeNull("a PDF must never be uploaded as an image");
        var parameters = gateway.LastRawUploadParameters!;
        parameters["type"].Should().Be("authenticated");
        parameters["overwrite"].Should().Be("false");
        ((string)parameters["tags"]).Should().Contain("kind:pdf");
        parameters["public_id"].Should()
            .Be(MediaTagger.BuildConversationPublicId(organizationId, attachment.Id));

        attachment.StorageProvider.Should().Be("cloudinary");
        attachment.StorageKey.Should().Be(
            $"raw/authenticated:{MediaTagger.BuildConversationPublicId(organizationId, attachment.Id)}.pdf");
    }

    [Fact]
    public async Task StoreAsync_Image_IsAnAuthenticatedImageUploadTaggedKindImage()
    {
        var gateway = new FakeCloudinaryGateway();
        var store = Store(CloudinaryStorage(gateway));
        var organizationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var attachment = await store.StoreAsync(Request(
            organizationId, conversationId, Guid.NewGuid(), MediaSource.Web, contentType: "image/png"));

        gateway.LastRawUploadParameters.Should().BeNull();
        var parameters = gateway.LastImageUploadParameters!;
        parameters["type"].Should().Be("authenticated");
        ((string)parameters["tags"]).Should().Contain("kind:image");

        attachment.StorageProvider.Should().Be("cloudinary");
        attachment.StorageKey.Should().Be(
            $"image/authenticated:{MediaTagger.BuildConversationPublicId(organizationId, attachment.Id)}");
    }

    // ---------------------------------------------------------------------------------------
    // The URL must not change (strategy §3.3), and the key is the provider's, never re-derived
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_KeepsTheAuthenticatedAvelineUrl_NotTheCloudinaryUrl()
    {
        var storage = new RecordingMediaStorage(request => new StoredMedia(
            "cloudinary",
            $"image/authenticated:{request.PublicId}",
            "https://res.cloudinary.com/a-cloud/image/authenticated/v1/aveline/x"));
        var store = Store(storage);
        var organizationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var attachment = await store.StoreAsync(Request(
            organizationId, conversationId, Guid.NewGuid(), MediaSource.Web));

        attachment.Url.Should().Be(
            $"/api/v1/orgs/{organizationId}/conversations/{conversationId}/attachments/{attachment.Id}");
        attachment.Url.Should().NotContain("cloudinary");
        attachment.Url.Should().NotContain("http");
    }

    [Fact]
    public async Task StoreAsync_TakesTheStorageKeyFromThePutResult_NotADerivedOne()
    {
        // Cloudinary appends a raw file's extension to the requested id (unit U1.1, verified
        // live), so the persisted key must be what the provider reported.
        var storage = new RecordingMediaStorage(request => new StoredMedia(
            "cloudinary",
            $"raw/authenticated:{request.PublicId}.pdf",
            "https://res.cloudinary.com/a-cloud/raw/authenticated/v1/aveline/x.pdf"));
        var store = Store(storage);
        var organizationId = Guid.NewGuid();

        var attachment = await store.StoreAsync(Request(
            organizationId, Guid.NewGuid(), Guid.NewGuid(), MediaSource.Web, contentType: "application/pdf"));

        attachment.StorageKey.Should().Be(
            $"raw/authenticated:{MediaTagger.BuildConversationPublicId(organizationId, attachment.Id)}.pdf");
    }

    // ---------------------------------------------------------------------------------------
    // Per-row dispatch (strategy §3.3): a database row still reads its own bytes
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenReadAsync_ForACloudinaryRow_FetchesThroughTheMediaStorage()
    {
        var storage = new RecordingMediaStorage();
        var store = Store(storage);
        var attachment = await store.StoreAsync(Request(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MediaSource.Web));

        var stream = await store.OpenReadAsync(attachment);

        stream.Should().NotBeNull();
        storage.LastOpen.Should().NotBeNull();
        storage.LastOpen!.Provider.Should().Be("cloudinary");
        storage.LastOpen.StorageKey.Should().Be(attachment.StorageKey);
    }

    [Fact]
    public async Task OpenReadAsync_ForADualWrittenRow_ServesTheRowsBytesWithoutTheProvider()
    {
        // Stage 1 (`DualWrite=true`, `ReadFromCloudinary=false`) keeps every read in the row, so a
        // provider outage is invisible (strategy §5.2 stage 1).
        var storage = new RecordingMediaStorage();
        var store = Store(storage, dualWrite: true);
        var bytes = new byte[] { 11, 22, 33 };
        var attachment = await store.StoreAsync(Request(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MediaSource.Web, bytes: bytes));

        var stream = await store.OpenReadAsync(attachment);

        storage.LastOpen.Should().BeNull("the dual-write copy is the stage-1 read");
        using var buffer = new MemoryStream();
        await stream!.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public async Task OpenReadAsync_ForADatabaseRow_ServesTheRowsBytesWithoutTheProvider()
    {
        var storage = new RecordingMediaStorage();
        var store = Store(storage);
        var row = new MessageAttachment
        {
            OrganizationId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            StorageProvider = "database",
            StorageKey = Guid.NewGuid().ToString(),
            ImageData = [4, 5, 6],
            ContentType = "image/png",
            FileName = "photo.png",
            Url = "/api/v1/orgs/x/conversations/y/attachments/z",
        };

        var stream = await store.OpenReadAsync(row);

        storage.LastOpen.Should().BeNull("a row the Cloudinary adapter did not write keeps its bytes");
        using var buffer = new MemoryStream();
        await stream!.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(4, 5, 6);
    }

    [Fact]
    public async Task DeleteAsync_ForACloudinaryRow_ReleasesTheProviderAsset()
    {
        var storage = new RecordingMediaStorage();
        var store = Store(storage);
        var attachment = await store.StoreAsync(Request(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MediaSource.Web));

        await store.DeleteAsync(attachment);

        storage.Deleted.Should().ContainSingle();
        storage.Deleted[0].Provider.Should().Be("cloudinary");
        storage.Deleted[0].StorageKey.Should().Be(attachment.StorageKey);
    }

    [Fact]
    public async Task DeleteAsync_ForADatabaseRow_DoesNotCallTheProvider()
    {
        var storage = new RecordingMediaStorage();
        var store = Store(storage);
        var row = new MessageAttachment
        {
            OrganizationId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            StorageProvider = "database",
            StorageKey = Guid.NewGuid().ToString(),
            ImageData = [1],
            ContentType = "image/png",
            FileName = "photo.png",
            Url = "/api/v1/orgs/x/conversations/y/attachments/z",
        };

        await store.DeleteAsync(row);

        storage.Deleted.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private CloudinaryAttachmentStore Store(IMediaStorage storage, bool dualWrite = false) =>
        new(
            _context,
            storage,
            Microsoft.Extensions.Options.Options.Create(new MediaOptions { DualWrite = dualWrite }),
            new FixedTimeProvider(Now));

    private static AttachmentStoreRequest Request(
        Guid organizationId,
        Guid conversationId,
        Guid? uploadedByUserId,
        MediaSource source,
        string contentType = "image/jpeg",
        Guid? customerId = null,
        byte[]? bytes = null)
    {
        var payload = bytes ?? [1, 2, 3];
        return new AttachmentStoreRequest(
            organizationId, conversationId, uploadedByUserId, payload, contentType, "photo.jpg",
            null, null, source, customerId, AttachmentContentHash.Compute(payload));
    }

    private static CloudinaryMediaStorage CloudinaryStorage(ICloudinaryGateway gateway)
    {
        var options = new CloudinaryOptions
        {
            CloudName = "a-cloud",
            ApiKey = "test-key",
            ApiSecret = "test-secret",
            CatalogDeliveryType = "upload",
            ProtectedDeliveryType = "authenticated",
            UploadRetryAttempts = 3,
        };

        return new CloudinaryMediaStorage(
            gateway,
            Microsoft.Extensions.Options.Options.Create(options),
            new CloudinaryRetryPolicy(options, NullLogger.Instance),
            NullLogger<CloudinaryMediaStorage>.Instance);
    }

    /// <summary>A clock the tagger's UTC day is asserted against, so the test is not midnight-flaky.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Records what the row seam handed the provider, and answers from a caller's rule.</summary>
    private sealed class RecordingMediaStorage(Func<MediaPutRequest, StoredMedia>? result = null)
        : IMediaStorage
    {
        private readonly Func<MediaPutRequest, StoredMedia> _result = result
            ?? (request => new StoredMedia(
                "cloudinary",
                $"image/authenticated:{request.PublicId}",
                "https://res.cloudinary.com/a-cloud/image/authenticated/v1/aveline/x"));

        public MediaPutRequest? LastPut { get; private set; }

        public StoredMediaRef? LastOpen { get; private set; }

        public List<StoredMediaRef> Deleted { get; } = [];

        public string Provider => "cloudinary";

        public Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default)
        {
            LastPut = request;
            return Task.FromResult(_result(request));
        }

        public Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default)
        {
            LastOpen = reference;
            return Task.FromResult<Stream?>(new MemoryStream([9, 8, 7], writable: false));
        }

        public Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default)
        {
            Deleted.Add(reference);
            return Task.CompletedTask;
        }

        public string PublicDeliveryUrl(StoredMediaRef reference) => "https://cdn/" + reference.StorageKey;

        public string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc) =>
            "https://cdn/signed/" + reference.StorageKey;
    }

    /// <summary>The SDK surface, faked: the request objects are recorded, no network is touched.</summary>
    private sealed class FakeCloudinaryGateway : ICloudinaryGateway
    {
        public bool Secure => true;

        public SortedDictionary<string, object>? LastImageUploadParameters { get; private set; }

        public SortedDictionary<string, object>? LastRawUploadParameters { get; private set; }

        public Task<CloudinaryAttemptResult> UploadImageAsync(ImageUploadParams parameters, CancellationToken ct)
        {
            LastImageUploadParameters = parameters.ToParamsDictionary();
            return Task.FromResult(Success(parameters.PublicId));
        }

        public Task<CloudinaryAttemptResult> UploadRawAsync(RawUploadParams parameters, CancellationToken ct)
        {
            LastRawUploadParameters = parameters.ToParamsDictionary();
            // Cloudinary appends the original file's extension to a raw public id (verified live).
            return Task.FromResult(Success($"{parameters.PublicId}.pdf"));
        }

        public Task<CloudinaryAttemptResult> DestroyAsync(DeletionParams parameters, CancellationToken ct) =>
            Task.FromResult(Success(parameters.PublicId));

        public Task<CloudinaryResourceInfo?> GetResourceAsync(
            string publicId, string resourceType, string deliveryType, CancellationToken ct) =>
            Task.FromResult<CloudinaryResourceInfo?>(new CloudinaryResourceInfo([], []));

        public string BuildDeliveryUrl(string publicId, string resourceType, string deliveryType, bool signed) =>
            $"https://res.cloudinary.com/a-cloud/{resourceType}/{deliveryType}/{publicId}";

        public string BuildPrivateDownloadUrl(
            string publicId, string resourceType, string deliveryType, DateTimeOffset expiresAtUtc) =>
            $"https://api.cloudinary.com/v1_1/a-cloud/{resourceType}/download?public_id={publicId}";

        public Task<Stream> FetchAsync(string url, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream([1], writable: false));

        private static CloudinaryAttemptResult Success(string publicId) =>
            new(200, null, publicId,
                "https://res.cloudinary.com/a-cloud/image/upload/v1/" + publicId, null, 3, false);
    }
}
