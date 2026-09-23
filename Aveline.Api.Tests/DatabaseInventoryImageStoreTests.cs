using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// U0.5 (lane L2) — the catalog row seam. <see cref="DatabaseInventoryImageStore"/> owns the
/// <see cref="InventoryImage"/> row and delegates the bytes to the provider seam
/// (<see cref="IMediaStorage"/>); it must not implement that seam, and it must not know any
/// provider's key format (strategy §3.1, "no class implements two seams").
/// </summary>
public class DatabaseInventoryImageStoreTests : IDisposable
{
    private static readonly byte[] Bytes = [1, 2, 3, 4, 5];

    private readonly AppDbContext _context;
    private readonly RecordingMediaStorage _media = new();
    private readonly DatabaseInventoryImageStore _store;
    private readonly InventoryRepository _repository;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly string _databaseName = $"InventoryImageStore_{Guid.NewGuid()}";

    public DatabaseInventoryImageStoreTests()
    {
        _context = NewInMemoryContext();
        _store = new DatabaseInventoryImageStore(_context, _media);
        _repository = new InventoryRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    private AppDbContext NewInMemoryContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: _databaseName)
            .Options);

    // The in-memory provider has no relational type mapping, so the model-level checks build the
    // design-time model against Npgsql (no connection is opened) — the repository's precedent in
    // ApiRequestMetricEntityConfigurationTests.
    private static AppDbContext NewPostgresModelContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options);

    private static IModel PostgresModel(AppDbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    private Task<InventoryImage> StoreAsync(byte[]? bytes = null, Guid? itemId = null)
    {
        var payload = bytes ?? Bytes;
        return _store.StoreAsync(new InventoryImageStoreRequest(
            _orgId, itemId, payload, "image/jpeg", "piece.jpg", payload.LongLength));
    }

    [Fact]
    public void Provider_IsTheDatabaseTier()
    {
        _store.Provider.Should().Be("database");
    }

    [Fact]
    public async Task StoreAsync_PersistsARowWhoseBytesRoundTrip()
    {
        var image = await StoreAsync();

        image.OrgId.Should().Be(_orgId);
        image.StorageProvider.Should().Be("database");
        image.ImageData.Should().Equal(Bytes);
        image.ContentType.Should().Be("image/jpeg");
        image.FileName.Should().Be("piece.jpg");
        image.FileSizeBytes.Should().Be(Bytes.LongLength);

        await using var reloaded = NewInMemoryContext();
        var row = await reloaded.InventoryImages.SingleAsync(i => i.Id == image.Id);
        row.ImageData.Should().Equal(Bytes);
    }

    [Fact]
    public async Task StoreAsync_WritesTheRelativeDatabaseTierRoute()
    {
        var image = await StoreAsync();

        image.ImageUrl.Should().Be($"/api/v1/orgs/{_orgId}/catalog/images/{image.Id}");
    }

    [Fact]
    public async Task StoreAsync_CarriesTheItemIdWhenThereIsOne()
    {
        var itemId = Guid.NewGuid();

        var image = await StoreAsync(itemId: itemId);

        image.ItemId.Should().Be(itemId);
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsTheStoredBytesAsANonWritableStream()
    {
        var image = await StoreAsync();

        using var stream = await _store.OpenReadAsync(image);

        stream.Should().NotBeNull();
        stream!.CanRead.Should().BeTrue();
        stream.CanWrite.Should().BeFalse();
        stream.CanSeek.Should().BeTrue();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(Bytes);
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsNullWhenTheRowCarriesNoBytes()
    {
        var image = await StoreAsync();
        image.ImageData = null;

        (await _store.OpenReadAsync(image)).Should().BeNull();
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsNullForAnEmptyByteArray()
    {
        var image = await StoreAsync(bytes: []);

        (await _store.OpenReadAsync(image)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_TellsTheProviderBeforeTheRowDisappears()
    {
        var image = await StoreAsync();
        var expected = new StoredMediaRef("database", string.Empty, "image/jpeg");

        var rowPresentWhenProviderTold = false;
        _media.OnDelete = _ =>
        {
            rowPresentWhenProviderTold = _context.InventoryImages.Any(i => i.Id == image.Id);
            return Task.CompletedTask;
        };

        // The caller's ordering, mirroring AttachmentSweepJob.cs:74-78: tell the store first, so a
        // provider that keeps bytes elsewhere releases them before the row that names them
        // disappears. The caller — not the store — removes the row (IAttachmentStore.cs:46).
        await _store.DeleteAsync(image);

        _media.Deleted.Should().ContainSingle();
        _media.Deleted[0].Should().Be(expected);
        rowPresentWhenProviderTold.Should().BeTrue();
        _context.InventoryImages.Any(i => i.Id == image.Id)
            .Should().BeTrue("the store never removes the row itself");

        await _repository.DeleteImageAsync(image.Id, image.OrgId);

        _context.InventoryImages.Any(i => i.Id == image.Id).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteImageAsync_LeavesOtherOrganisationsRowsAlone()
    {
        var image = await StoreAsync();

        await _repository.DeleteImageAsync(image.Id, Guid.NewGuid());

        _context.InventoryImages.Any(i => i.Id == image.Id)
            .Should().BeTrue("the delete is tenant-scoped");
    }

    [Fact]
    public void Store_ImplementsOnlyTheRowSeam()
    {
        // Strategy §3.1: no class implements two seams. The bytes are delegated, not implemented.
        typeof(DatabaseInventoryImageStore).GetInterfaces()
            .Should().Equal(typeof(IInventoryImageStore));
    }

    [Fact]
    public void StorageProvider_IsRequiredWithTheDatabaseDefault()
    {
        using var context = NewPostgresModelContext();
        var property = PostgresModel(context).FindEntityType(typeof(InventoryImage))!
            .FindProperty(nameof(InventoryImage.StorageProvider))!;

        property.IsNullable.Should().BeFalse();
        property.GetDefaultValue().Should().Be("database");
        property.GetMaxLength().Should().Be(32);
        property.GetColumnType().Should().Be("character varying(32)");
    }

    [Fact]
    public void StorageKey_IsNullableAtTwoHundredCharacters()
    {
        using var context = NewPostgresModelContext();
        var property = PostgresModel(context).FindEntityType(typeof(InventoryImage))!
            .FindProperty(nameof(InventoryImage.StorageKey))!;

        property.IsNullable.Should().BeTrue();
        property.GetMaxLength().Should().Be(200);
        property.GetColumnType().Should().Be("character varying(200)");
    }

    /// <summary>
    /// The provider seam, recorded rather than mocked, so the ordering of the delegation is
    /// observable. Implements the seam once, in the fake — the store under test does not.
    /// </summary>
    private sealed class RecordingMediaStorage : IMediaStorage
    {
        public string Provider => "database";

        public List<StoredMediaRef> Deleted { get; } = [];

        public Func<StoredMediaRef, Task>? OnDelete { get; set; }

        public Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default) =>
            Task.FromResult(new StoredMedia(Provider, request.PublicId, request.PublicId));

        public Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public async Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default)
        {
            Deleted.Add(reference);
            if (OnDelete is not null)
            {
                await OnDelete(reference);
            }
        }

        public string PublicDeliveryUrl(StoredMediaRef reference) => reference.StorageKey;

        public string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc) =>
            reference.StorageKey;
    }
}
