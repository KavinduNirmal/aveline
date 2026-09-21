using System.Reflection;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.Models;
using FluentAssertions;

namespace Aveline.Api.Tests;

/// <summary>
/// S0 / U0.7 — <see cref="DatabaseMediaStorage"/>, the provider seam's database implementation
/// (strategy §3.1, §5.1 S0).
/// <para>
/// The database tier keeps the durable bytes on the row, written and opened by the row seams
/// (<c>DatabaseAttachmentStore</c>, <c>DatabaseInventoryImageStore</c>). This class is the
/// provider seam for that tier — deliberately a pass-through, and deliberately the one used by
/// the tests and by the <c>Media:Provider=database</c> rollback. It is therefore asserted to be
/// a <em>provider</em> seam only: it implements <see cref="IMediaStorage"/> and nothing else,
/// and it neither knows nor names an <see cref="InventoryImage"/> or
/// <see cref="MessageAttachment"/> row ("no class implements two seams", strategy §3.1).
/// </para>
/// <para>
/// The bytes are held in-process for the lifetime of the instance so the seam's
/// put → open → delete contract is exercisable; the row is still the durable copy on the
/// database tier. A reference this instance did not write reads as <c>null</c>, which is the
/// seam's documented "the bytes live elsewhere" answer (<c>IAttachmentStore.cs:22-26</c>).
/// </para>
/// </summary>
public class DatabaseMediaStorageTests
{
    /// <summary>
    /// The database tier's serving location, exactly as the shipped routes spell it:
    /// <c>/api/v1/orgs/{orgId}/catalog/images/{imageId}</c>. The database provider has no CDN
    /// origin, so the public id it is handed is the Aveline route the row is served through.
    /// </summary>
    private const string CatalogRoute =
        "/api/v1/orgs/11111111-1111-1111-1111-111111111111/catalog/images/22222222-2222-2222-2222-222222222222";

    private static readonly byte[] Bytes = [1, 2, 3, 4, 5];

    private readonly DatabaseMediaStorage _storage = new();

    [Fact]
    public void Provider_IsTheDatabaseTier()
    {
        _storage.Provider.Should().Be("database");
    }

    [Fact]
    public void Storage_ImplementsOnlyTheProviderSeam()
    {
        // Strategy §3.1: no class implements two seams. The row seams own the rows; this class
        // owns the bytes for the database tier.
        typeof(DatabaseMediaStorage).GetInterfaces().Should().Equal(typeof(IMediaStorage));
    }

    [Fact]
    public void Storage_TakesNoRowDependency()
    {
        // A provider seam that needed AppDbContext (or a row type) to do its job would be a row
        // seam in disguise. The database tier's durability comes from the row, not from here.
        var constructorParameters = typeof(DatabaseMediaStorage)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().NotContain(typeof(AppDbContext));
    }

    [Fact]
    public void Storage_NamesNoRowTypeInAnyOfItsOwnSignatures()
    {
        // "Touches no row type" (strategy §3.1) is asserted mechanically rather than promised:
        // no method parameter, return type or field of this class may mention a row model.
        Type[] rowTypes = [typeof(InventoryImage), typeof(MessageAttachment)];

        var storage = typeof(DatabaseMediaStorage);
        BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var mentioned = storage.GetMethods(declared)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .Concat(storage.GetFields(declared).Select(field => field.FieldType))
            .ToArray();

        mentioned.Should().NotContain(type => rowTypes.Contains(type));
    }

    [Fact]
    public async Task PutOpenDelete_RoundTripsTheBytes()
    {
        var stored = await _storage.PutAsync(Request());

        stored.Provider.Should().Be("database");
        stored.StorageKey.Should().Be(CatalogRoute);
        stored.Url.Should().Be(CatalogRoute, "the database tier's URL is the Aveline route the row is served through");

        using var stream = await _storage.OpenReadAsync(Reference(stored));

        stream.Should().NotBeNull();
        stream!.CanRead.Should().BeTrue();
        stream.CanWrite.Should().BeFalse();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(Bytes);

        await _storage.DeleteAsync(Reference(stored));

        (await _storage.OpenReadAsync(Reference(stored))).Should().BeNull();
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsNullForAReferenceThisStoreNeverWrote()
    {
        var reference = new StoredMediaRef("database", "/api/v1/orgs/unknown/catalog/images/unknown", "image/jpeg");

        var stream = await _storage.OpenReadAsync(reference);

        stream.Should().BeNull("the database tier's bytes live on the row, not in this instance");
    }

    [Fact]
    public async Task PutAsync_AcceptsAndIgnoresMediaMetadata()
    {
        var metadata = new MediaMetadata(
            ["catalog-image", "kind:image", "organizationId:org-1"],
            new Dictionary<string, string> { ["o"] = "org-1", ["s"] = "catalog" });

        // A byte store has nowhere to put provider-neutral labels and attributes, and the record's
        // own doc comment allows ignoring them (strategy §3.2). The put must not throw, and the
        // result must be indistinguishable from the same put with no metadata at all.
        var withMetadata = await _storage.PutAsync(Request(metadata));
        var withoutMetadata = await _storage.PutAsync(Request(metadata: null));

        withMetadata.Should().Be(withoutMetadata);

        using var stream = await _storage.OpenReadAsync(Reference(withMetadata));
        stream.Should().NotBeNull();
    }

    [Fact]
    public void PublicDeliveryUrl_IsTheRouteRelativeAvelineUrl()
    {
        var reference = new StoredMediaRef("database", CatalogRoute, "image/jpeg");

        var url = _storage.PublicDeliveryUrl(reference);

        url.Should().Be(CatalogRoute);
        url.Should().StartWith("/api/v1/", "the database tier serves through the Aveline API, not a CDN");
    }

    [Fact]
    public void PublicDeliveryUrl_PassesAnAbsoluteUrlThroughUnchanged()
    {
        const string absolute = "https://api.aveline.example/api/v1/orgs/org-1/catalog/images/image-1";
        var reference = new StoredMediaRef("database", absolute, "image/jpeg");

        _storage.PublicDeliveryUrl(reference).Should().Be(absolute);
    }

    [Fact]
    public void SignedDeliveryUrl_IsTheSameAvelineRouteForTheDatabaseTier()
    {
        // The database tier has no CDN signing: the route is guarded by the API's own
        // authentication, so the "signed" URL is the same route and the expiry does not apply.
        var reference = new StoredMediaRef("database", CatalogRoute, "image/jpeg");

        _storage.SignedDeliveryUrl(reference, DateTimeOffset.UtcNow.AddMinutes(5))
            .Should().Be(CatalogRoute);
    }

    private static MediaPutRequest Request(MediaMetadata? metadata = null) =>
        new(CatalogRoute, Bytes, "image/jpeg", "piece.jpg", MediaTier.Public, metadata);

    private static StoredMediaRef Reference(StoredMedia stored) =>
        new(stored.Provider, stored.StorageKey, "image/jpeg");
}
