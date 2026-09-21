using Aveline.Api.Common.Media;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using CloudinaryDotNet.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// The thread's attachments (D8): the content-type policy, the store boundary, the binding
/// rules and the orphan sweep.
/// </summary>
public class ConversationAttachmentTests
{
    private static readonly byte[] Bytes = [1, 2, 3, 4, 5];

    private readonly AppDbContext _context;
    private readonly MessageAttachmentRepository _attachments;
    private readonly DatabaseAttachmentStore _store;
    private readonly Guid _orgId;
    private readonly Guid _conversationId;

    public ConversationAttachmentTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Attachments_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _attachments = new MessageAttachmentRepository(_context);
        _store = new DatabaseAttachmentStore(_context);
        _orgId = Guid.NewGuid();
        _conversationId = new ConversationRepository(_context)
            .GetOrCreateSalonAsync(_orgId, Guid.NewGuid(), null, "thread-attach-1")
            .GetAwaiter().GetResult().Conversation.Id;
    }

    private Task<MessageAttachment> UploadAsync(string contentType = "image/jpeg", string fileName = "photo.jpg")
        => _store.StoreAsync(new AttachmentStoreRequest(
            _orgId, _conversationId, Guid.NewGuid(), Bytes, contentType, fileName, null, null,
            MediaSource.Web, null, AttachmentContentHash.Compute(Bytes)));

    [Fact]
    public async Task StoreAsync_WritesTheRowWithTheDatabaseProviderAndAnAuthenticatedUrl()
    {
        var attachment = await UploadAsync();

        Assert.Equal("database", attachment.StorageProvider);
        Assert.Equal(attachment.Id.ToString(), attachment.StorageKey);
        Assert.Equal(Bytes.LongLength, attachment.SizeBytes);
        Assert.Contains($"/conversations/{_conversationId}/attachments/{attachment.Id}", attachment.Url);
        Assert.Null(attachment.MessageId);
    }

    [Fact]
    public async Task OpenReadAsync_ServesTheStoredBytes()
    {
        var attachment = await UploadAsync();

        var stream = await _store.OpenReadAsync(attachment);

        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        await stream!.CopyToAsync(buffer);
        Assert.Equal(Bytes, buffer.ToArray());
    }

    [Fact]
    public async Task GetBindableAsync_OnlyReturnsThisConversationsUnboundRows()
    {
        var mine = await UploadAsync();
        var other = new ConversationRepository(_context)
            .GetOrCreateSalonAsync(_orgId, Guid.NewGuid(), Guid.NewGuid(), "thread-attach-other")
            .GetAwaiter().GetResult().Conversation.Id;
        var foreign = await _store.StoreAsync(new AttachmentStoreRequest(
            _orgId, other, Guid.NewGuid(), Bytes, "image/png", "other.png", null, null,
            MediaSource.Web, null, AttachmentContentHash.Compute(Bytes)));
        var bound = await UploadAsync();
        bound.MessageId = Guid.NewGuid();
        await _attachments.SaveAsync(bound);

        var bindable = await _attachments.GetBindableAsync(_orgId, _conversationId, [mine.Id, foreign.Id, bound.Id]);

        var only = Assert.Single(bindable);
        Assert.Equal(mine.Id, only.Id);
    }

    [Fact]
    public async Task DeleteOrphansAsync_RemovesOnlyUnboundRowsPastTheCutoff()
    {
        var orphan = await UploadAsync();
        orphan.CreatedAtUtc = DateTime.UtcNow.AddHours(-25);
        await _context.SaveChangesAsync();
        var fresh = await UploadAsync();
        var bound = await UploadAsync();
        bound.MessageId = Guid.NewGuid();
        await _attachments.SaveAsync(bound);

        var removed = await _attachments.DeleteOrphansAsync(DateTime.UtcNow.AddHours(-24));

        Assert.Equal(1, removed);
        var remaining = await _context.MessageAttachments.ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, a => a.Id == orphan.Id);
        Assert.Contains(remaining, a => a.Id == fresh.Id);
        Assert.Contains(remaining, a => a.Id == bound.Id);
    }

    [Fact]
    public async Task CloudinaryStore_UploadsThroughTheProviderSeamsBoundedRetry()
    {
        // U2.3: the row seam adds no retry of its own. It hands the bytes to `IMediaStorage`,
        // whose Cloudinary implementation owns the bounded `420`/5xx policy (risk R22), so a
        // rate-limited upload succeeds on the second attempt without a second retry loop here.
        var gateway = new RecordingCloudinaryGateway(failFirstWith: 420);
        var storage = CloudinaryStorage(gateway);
        var store = new CloudinaryAttachmentStore(
            _context,
            storage,
            Microsoft.Extensions.Options.Options.Create(new MediaOptions()),
            TimeProvider.System);

        var attachment = await store.StoreAsync(new AttachmentStoreRequest(
            _orgId, _conversationId, Guid.NewGuid(), Bytes, "image/jpeg", "photo.jpg", null, null,
            MediaSource.Web, null, AttachmentContentHash.Compute(Bytes)));

        Assert.Equal(2, gateway.UploadAttempts);
        Assert.Equal("cloudinary", attachment.StorageProvider);
        Assert.Contains(attachment.Id.ToString(), attachment.StorageKey);
        Assert.Equal(1, await _context.MessageAttachments.CountAsync(a => a.Id == attachment.Id));
    }

    private static CloudinaryMediaStorage CloudinaryStorage(ICloudinaryGateway gateway)
    {
        var options = new Aveline.Api.Modules.Media.CloudinaryOptions
        {
            CloudName = "a-cloud",
            ApiKey = "test-key",
            ApiSecret = "test-secret",
            UploadRetryAttempts = 3,
        };

        return new CloudinaryMediaStorage(
            gateway,
            Microsoft.Extensions.Options.Options.Create(options),
            new CloudinaryRetryPolicy(options, NullLogger.Instance, new NoOpRetryDelay()),
            NullLogger<CloudinaryMediaStorage>.Instance);
    }

    /// <summary>Fails the first upload with a retryable status, then accepts.</summary>
    private sealed class RecordingCloudinaryGateway(int failFirstWith) : ICloudinaryGateway
    {
        public int UploadAttempts { get; private set; }

        public bool Secure => true;

        public Task<CloudinaryAttemptResult> UploadImageAsync(ImageUploadParams parameters, CancellationToken ct)
        {
            UploadAttempts++;
            return Task.FromResult(UploadAttempts == 1
                ? new CloudinaryAttemptResult(failFirstWith, "rate limited", null, null, null, 0, false)
                : new CloudinaryAttemptResult(
                    200, null, parameters.PublicId,
                    "https://res.cloudinary.com/a-cloud/image/authenticated/v1/" + parameters.PublicId,
                    null, Bytes.LongLength, false));
        }

        public Task<CloudinaryAttemptResult> UploadRawAsync(RawUploadParams parameters, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<CloudinaryAttemptResult> DestroyAsync(DeletionParams parameters, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<CloudinaryResourceInfo?> GetResourceAsync(
            string publicId, string resourceType, string deliveryType, CancellationToken ct)
            => throw new NotSupportedException();

        public string BuildDeliveryUrl(string publicId, string resourceType, string deliveryType, bool signed)
            => $"https://res.cloudinary.com/a-cloud/{resourceType}/{deliveryType}/{publicId}";

        public string BuildPrivateDownloadUrl(
            string publicId, string resourceType, string deliveryType, DateTimeOffset expiresAtUtc)
            => $"https://api.cloudinary.com/v1_1/a-cloud/{resourceType}/download?public_id={publicId}";

        public Task<Stream> FetchAsync(string url, CancellationToken ct)
            => throw new NotSupportedException();
    }

    /// <summary>The backoff without its wall-clock cost; the retry count is what is asserted.</summary>
    private sealed class NoOpRetryDelay : IMediaRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task SweepAsync_DeletesAnOrphanAndKeepsABoundRow()
    {
        // The name is hoisted: evaluated inside the options lambda it would mint a fresh
        // database per context, and the sweep would look at an empty one.
        var databaseName = $"Sweep_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IMessageAttachmentRepository, MessageAttachmentRepository>();
        services.AddScoped<IAttachmentStore, DatabaseAttachmentStore>();
        await using var provider = services.BuildServiceProvider();

        Guid orphanId;
        Guid boundId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IAttachmentStore>();
            var repository = scope.ServiceProvider.GetRequiredService<IMessageAttachmentRepository>();
            var conversationId = new ConversationRepository(context)
                .GetOrCreateSalonAsync(Guid.NewGuid(), Guid.NewGuid(), null, "thread-sweep")
                .GetAwaiter().GetResult().Conversation.Id;
            var orgId = Guid.NewGuid();

            var orphan = await store.StoreAsync(new AttachmentStoreRequest(
                orgId, conversationId, null, Bytes, "image/jpeg", "orphan.jpg", null, null,
                MediaSource.Web, null, AttachmentContentHash.Compute(Bytes)));
            orphan.CreatedAtUtc = DateTime.UtcNow - AttachmentSweepJob.Ttl - TimeSpan.FromHours(1);
            var bound = await store.StoreAsync(new AttachmentStoreRequest(
                orgId, conversationId, null, Bytes, "image/jpeg", "bound.jpg", null, null,
                MediaSource.Web, null, AttachmentContentHash.Compute(Bytes)));
            bound.MessageId = Guid.NewGuid();
            await context.SaveChangesAsync();
            await repository.SaveAsync(bound);

            orphanId = orphan.Id;
            boundId = bound.Id;
        }

        var job = new AttachmentSweepJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AttachmentSweepJob>.Instance);

        var swept = await job.SweepAsync();

        Assert.Equal(1, swept);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await context.MessageAttachments.ToListAsync();
            Assert.Single(rows);
            Assert.Equal(boundId, rows[0].Id);
            Assert.DoesNotContain(rows, a => a.Id == orphanId);
        }
    }
}

public class MediaContentTypesTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("IMAGE/PNG")]
    public void IsAllowed_AcceptsTheAllowList(string contentType)
        => Assert.True(MediaContentTypes.IsAllowed(contentType));

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("video/mp4")]
    [InlineData("audio/ogg")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public void IsAllowed_RejectsEverythingElse(string? contentType)
        => Assert.False(MediaContentTypes.IsAllowed(contentType));

    [Fact]
    public void Resolve_KeepsAnAllowedDeclaredType()
        => Assert.Equal("application/pdf", MediaContentTypes.Resolve("application/pdf", "report.pdf"));

    [Fact]
    public void Resolve_FallsBackToTheExtensionForAGenericType()
        => Assert.Equal("image/png", MediaContentTypes.Resolve("application/octet-stream", "photo.PNG"));

    [Fact]
    public void Resolve_RefusesADisallowedDeclaredTypeEvenWhenTheExtensionLooksFine()
    {
        // The uploader said what it is; a picture-shaped file name does not rescue a declared
        // `text/html`.
        Assert.Null(MediaContentTypes.Resolve("text/html", "photo.png"));
    }

    [Fact]
    public void Resolve_RefusesAnUnknownExtensionWithNoDeclaredType()
        => Assert.Null(MediaContentTypes.Resolve(null, "script.js"));

    [Fact]
    public void SafeServe_NeverEchoesADisallowedStoredType()
    {
        Assert.Equal("application/octet-stream", MediaContentTypes.SafeServe("text/html"));
        Assert.Equal("application/pdf", MediaContentTypes.SafeServe("application/pdf"));
    }

    [Fact]
    public void NormalizeImage_StillRefusesAPdfForTheCatalog()
    {
        // The catalog's rule is unchanged: product images only, even though the shared policy
        // admits a PDF for a thread attachment.
        Assert.Equal(MediaContentTypes.Fallback, MediaContentTypes.NormalizeImage("application/pdf"));
        Assert.Equal("image/png", MediaContentTypes.NormalizeImage("image/png"));
    }
}
