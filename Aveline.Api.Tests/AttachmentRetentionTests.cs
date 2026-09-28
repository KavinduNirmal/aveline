using System.Globalization;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// S7 / U4.1 — the conversation attachment retention policy. These are the decisive tests of
/// strategy §7 check 8: a bound attachment past the window is gone from both the row and the
/// store; one inside is untouched; a run never exceeds its ceiling; a store failure leaves the
/// row rather than deleting it first; a live conversation is swept (the pinned semantics); and —
/// the assertion that replaces the deleted tag-reconciliation job — after a week of simulated
/// traffic no asset under <c>aveline/</c> lacks its labels.
/// </summary>
public class AttachmentRetentionTests
{
    private static readonly byte[] Bytes = [1, 2, 3, 4, 5];

    private static Harness Build(int? maxPerRun = null, int? retentionDays = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConversationAttachmentRetentionJob.RetentionDaysConfigurationKey] =
                    (retentionDays ?? ConversationAttachmentRetentionJob.DefaultRetentionDays)
                    .ToString(CultureInfo.InvariantCulture),
                [ConversationAttachmentRetentionJob.MaxPerRunConfigurationKey] =
                    (maxPerRun ?? ConversationAttachmentRetentionJob.DefaultMaxPerRun)
                    .ToString(CultureInfo.InvariantCulture),
            })
            .Build();

        var databaseName = $"Retention_{Guid.NewGuid()}";
        var storage = new RecordingMediaStorage();
        var clock = new MutableTimeProvider();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IMessageAttachmentRepository, MessageAttachmentRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IAttachmentStore>(sp => new CloudinaryAttachmentStore(
            sp.GetRequiredService<AppDbContext>(),
            storage,
            Options.Create(new MediaOptions()),
            clock));

        var provider = services.BuildServiceProvider();
        var job = new ConversationAttachmentRetentionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConversationAttachmentRetentionJob>.Instance);

        return new Harness(provider, databaseName, storage, clock, job);
    }

    // 1. A bound attachment past the window is gone from both the row and the store.
    [Fact]
    public async Task BoundAttachmentPastTheWindow_IsGoneFromTheRowAndTheStore()
    {
        using var harness = Build();
        var orgId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var expired = await SeedBoundAsync(
            harness, orgId, conversationId, DateTimeOffset.UtcNow.AddDays(-10), MediaSource.Web);
        var fresh = await SeedBoundAsync(
            harness, orgId, conversationId, DateTimeOffset.UtcNow.AddMinutes(-1), MediaSource.Web);

        var swept = await harness.Job.RunAsync();

        Assert.Equal(1, swept);
        await using var context = harness.NewContext();
        var remaining = await context.MessageAttachments.ToListAsync();
        Assert.Equal(fresh.Id, Assert.Single(remaining).Id);

        // The remote asset was released too, and the exact encoded key the row carried is what
        // the store was told to delete.
        var released = Assert.Single(harness.Storage.Deletes);
        Assert.Equal(expired.StorageKey, released.StorageKey);
    }

    // 2. One inside the window is untouched — row and store both.
    [Fact]
    public async Task BoundAttachmentInsideTheWindow_IsUntouched()
    {
        using var harness = Build();
        var inside = await SeedBoundAsync(
            harness, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-6), MediaSource.Web);

        var swept = await harness.Job.RunAsync();

        Assert.Equal(0, swept);
        Assert.Empty(harness.Storage.Deletes);
        await using var context = harness.NewContext();
        Assert.True(await context.MessageAttachments.AnyAsync(a => a.Id == inside.Id));
    }

    // 3. A run never exceeds its ceiling.
    [Fact]
    public async Task ARun_NeverExceedsItsCeiling()
    {
        using var harness = Build(maxPerRun: 3);
        var orgId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await SeedBoundAsync(
                harness, orgId, conversationId,
                DateTimeOffset.UtcNow.AddDays(-10).AddMinutes(-i), MediaSource.Web);
        }

        var first = await harness.Job.RunAsync();
        var second = await harness.Job.RunAsync();

        Assert.Equal(3, first);
        Assert.Equal(2, second);
        await using var context = harness.NewContext();
        Assert.Equal(0, await context.MessageAttachments.CountAsync());
    }

    // 4. A store failure leaves the row, and the store was told while the row still existed.
    [Fact]
    public async Task StoreFailure_LeavesTheRow()
    {
        using var harness = Build();
        var expired = await SeedBoundAsync(
            harness, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-10), MediaSource.Web);

        harness.Storage.RowStillPresentAtDelete = reference =>
        {
            using var context = harness.NewContext();
            return context.MessageAttachments.Any(a => a.Id == AttachmentIdFrom(reference.StorageKey));
        };
        harness.Storage.DeleteFailure = _ => new InvalidOperationException("The provider is down.");

        var swept = await harness.Job.RunAsync();

        Assert.Equal(0, swept);

        // The store was told (so this is the ordering path, not a store that was never called)...
        Assert.Single(harness.Storage.Deletes);

        // ...and at that instant the row was still present, which is the ordering
        // AttachmentSweepJob.cs:74-78 applies and risk R23 demands.
        Assert.All(harness.Storage.RowPresentWhenDeleted, present => Assert.True(present));

        // A failure releases nothing, so the row survives for the next run.
        await using var context = harness.NewContext();
        Assert.True(await context.MessageAttachments.AnyAsync(a => a.Id == expired.Id));
    }

    // 5. The pinned live-conversation rule: retention is per attachment, so an active thread is
    //    swept too. A later message never resets an older attachment's clock.
    [Fact]
    public async Task ALiveConversationsAttachment_IsStillSwept()
    {
        using var harness = Build();
        var orgId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        await using (var context = harness.NewContext())
        {
            context.Conversations.Add(new Conversation
            {
                Id = conversationId,
                OrganizationId = orgId,
                ThreadId = "thread-retention-live",
                LastMessageAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        var expired = await SeedBoundAsync(
            harness, orgId, conversationId, DateTimeOffset.UtcNow.AddDays(-10), MediaSource.Web);
        var recent = await SeedBoundAsync(
            harness, orgId, conversationId, DateTimeOffset.UtcNow.AddHours(-1), MediaSource.Web);

        var swept = await harness.Job.RunAsync();

        Assert.Equal(1, swept);
        await using var verify = harness.NewContext();
        var rows = await verify.MessageAttachments.ToListAsync();
        Assert.Equal(recent.Id, Assert.Single(rows).Id);
        Assert.NotEqual(expired.Id, rows[0].Id);
        Assert.Equal(conversationId, rows[0].ConversationId);
    }

    // 6. The assertion that replaces the deleted tag-reconciliation job (strategy §5.1 S7,
    //    §7 check 8): after a week of simulated traffic, no asset under `aveline/` lacks its
    //    labels. This is the detector for risk R16.
    [Fact]
    public async Task AfterAWeekOfTraffic_NoAssetUnderAvelineLacksItsLabels()
    {
        using var harness = Build();
        var orgId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var sources = new[] { MediaSource.Web, MediaSource.WhatsApp, MediaSource.Url };
        var now = DateTimeOffset.UtcNow;

        for (var day = 9; day >= 0; day--)
        {
            await SeedBoundAsync(
                harness, orgId, conversationId, now.AddDays(-day), sources[day % sources.Length]);
            await SeedCatalogAsync(harness, orgId);
        }

        // The week included retention passes, so some of those assets were swept as they aged.
        await harness.Job.RunAsync();
        Assert.NotEmpty(harness.Storage.Deletes);

        Assert.True(harness.Storage.Puts.Count >= 20);
        Assert.Contains(harness.Storage.Puts, p =>
            p.Metadata?.Labels.Contains(MediaTagger.SalonImageLabel) == true);
        Assert.Contains(harness.Storage.Puts, p =>
            p.Metadata?.Labels.Contains(MediaTagger.CatalogImageLabel) == true);

        AssertEveryAssetCarriesItsLabels(harness.Storage.Puts);
    }

    // 6b. The detector is falsifiable: an asset written with no metadata fails it.
    [Fact]
    public void TheUntaggedAssetDetector_RejectsAnAssetWrittenWithoutMetadata()
    {
        var untagged = new MediaPutRequest(
            "aveline/org/conversations/attachment",
            Bytes,
            "image/jpeg",
            "photo.jpg",
            MediaTier.Protected,
            Metadata: null);

        var failure = Record.Exception(() => AssertEveryAssetCarriesItsLabels([untagged]));

        Assert.NotNull(failure);
    }

    // 7. The catalog-side exemption: a catalog asset is not conversation content and is never
    //    swept, however old it is.
    [Fact]
    public async Task ACatalogAssetPastTheWindow_IsNotSwept()
    {
        using var harness = Build();
        var orgId = Guid.NewGuid();
        var catalog = await SeedCatalogAsync(harness, orgId);
        await SeedBoundAsync(
            harness, orgId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-10), MediaSource.Web);

        var swept = await harness.Job.RunAsync();

        Assert.Equal(1, swept);
        await using var context = harness.NewContext();
        Assert.True(await context.InventoryImages.AnyAsync(i => i.Id == catalog.Id));
        Assert.Equal(0, await context.MessageAttachments.CountAsync());
        Assert.DoesNotContain(harness.Storage.Deletes, reference =>
            reference.StorageKey.Contains("/catalog/", StringComparison.Ordinal));
    }

    // 8. The module registers the job as a hosted service, in the established style.
    [Fact]
    public void ConversationsModule_RegistersTheRetentionJobAsAHostedService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConversationAttachmentRetentionJob.RetentionDaysConfigurationKey] = "7",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock());
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"RetentionRegistration_{Guid.NewGuid()}"));
        services.AddConversationsModule(configuration);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(ConversationAttachmentRetentionJob));

        // The two jobs stay separate: the retention job does not replace the orphan sweep.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(AttachmentSweepJob));
    }

    /// <summary>
    /// R16's detector: every write through the provider seam must carry the labels
    /// <see cref="MediaTagger"/> built for its tier. An asset written outside the seam — or with
    /// <c>Metadata</c> dropped — fails here.
    /// </summary>
    private static void AssertEveryAssetCarriesItsLabels(IReadOnlyCollection<MediaPutRequest> writes)
    {
        Assert.NotEmpty(writes);
        foreach (var write in writes)
        {
            Assert.StartsWith("aveline/", write.PublicId);
            Assert.NotNull(write.Metadata);

            var labels = write.Metadata!.Labels;
            Assert.Contains(labels, label =>
                label == MediaTagger.SalonImageLabel || label == MediaTagger.CatalogImageLabel);
            Assert.Contains(labels, label => label.StartsWith("organizationId:", StringComparison.Ordinal));
            Assert.True(write.Metadata.Attributes.TryGetValue("o", out var organisation));
            Assert.False(string.IsNullOrWhiteSpace(organisation));
        }
    }

    private static async Task<SeededAttachment> SeedBoundAsync(
        Harness harness,
        Guid orgId,
        Guid conversationId,
        DateTimeOffset createdAtUtc,
        MediaSource source)
    {
        harness.Clock.Now = createdAtUtc;
        await using var scope = harness.Provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttachmentStore>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attachment = await store.StoreAsync(new AttachmentStoreRequest(
            orgId, conversationId, Guid.NewGuid(), Bytes, "image/jpeg", "photo.jpg", null, null,
            source, null, AttachmentContentHash.Compute(Bytes)));

        // Bound: the retention job never touches an unbound row (AttachmentSweepJob owns that).
        attachment.MessageId = Guid.NewGuid();
        await context.SaveChangesAsync();

        return new SeededAttachment(attachment.Id, attachment.StorageKey!);
    }

    private static async Task<InventoryImage> SeedCatalogAsync(Harness harness, Guid orgId)
    {
        await using var scope = harness.Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = new CloudinaryInventoryImageStore(
            harness.Storage,
            new InventoryRepository(context),
            Options.Create(new MediaOptions()));

        return await store.StoreAsync(new InventoryImageStoreRequest(
            orgId, null, Bytes, "image/jpeg", "product.jpg", Bytes.LongLength));
    }

    private static Guid AttachmentIdFrom(string storageKey)
    {
        var publicId = MediaStorageKey.PublicId(storageKey);
        return Guid.Parse(publicId[(publicId.LastIndexOf('/') + 1)..]);
    }

    private sealed record SeededAttachment(Guid Id, string StorageKey);

    private sealed class Harness(
        ServiceProvider provider,
        string databaseName,
        RecordingMediaStorage storage,
        MutableTimeProvider clock,
        ConversationAttachmentRetentionJob job) : IDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public RecordingMediaStorage Storage { get; } = storage;
        public MutableTimeProvider Clock { get; } = clock;
        public ConversationAttachmentRetentionJob Job { get; } = job;

        public AppDbContext NewContext() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);

        public void Dispose() => Provider.Dispose();
    }

    /// <summary>A clock the test drives, so "a week of traffic" has real timestamps.</summary>
    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// The provider seam, recorded rather than remote. It answers writes with the encoded
    /// <c>{resourceType}/{deliveryType}:{publicId}</c> key the row adapters expect, and it can be
    /// made to fail a delete to prove the store-before-row ordering.
    /// </summary>
    private sealed class RecordingMediaStorage : IMediaStorage
    {
        public List<MediaPutRequest> Puts { get; } = [];
        public List<StoredMediaRef> Deletes { get; } = [];
        public List<bool> RowPresentWhenDeleted { get; } = [];
        public Func<StoredMediaRef, bool>? RowStillPresentAtDelete { get; set; }
        public Func<StoredMediaRef, Exception?>? DeleteFailure { get; set; }

        public string Provider => "cloudinary";

        public Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default)
        {
            Puts.Add(request);
            var kind = request.Tier == MediaTier.Public ? "image/upload" : "image/authenticated";
            return Task.FromResult(new StoredMedia(
                Provider,
                $"{kind}:{request.PublicId}",
                $"https://res.cloudinary.com/demo/{kind}/v1/{request.PublicId}"));
        }

        public Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default)
        {
            Deletes.Add(reference);
            if (RowStillPresentAtDelete is { } probe)
            {
                RowPresentWhenDeleted.Add(probe(reference));
            }

            var failure = DeleteFailure?.Invoke(reference);
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }

        public string PublicDeliveryUrl(StoredMediaRef reference) =>
            $"https://res.cloudinary.com/demo/{reference.StorageKey}";

        public string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc) =>
            PublicDeliveryUrl(reference);
    }
}
