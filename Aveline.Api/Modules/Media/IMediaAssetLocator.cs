namespace Aveline.Api.Modules.Media;

/// <summary>
/// Locates the row a token names. An interface so the access service is testable without EF, and
/// so the two row types are reached through one seam rather than two branches in the route.
/// </summary>
/// <remarks>
/// Both lookups are tenant-scoped: a cross-org reference returns <c>null</c>, which the route
/// answers as <c>404</c> — never a token, never an asset (migration plan §7.5).
/// </remarks>
public interface IMediaAssetLocator
{
    /// <summary>
    /// The row carrying <paramref name="assetKey"/> for <paramref name="organizationId"/>, or
    /// <c>null</c> when there is no such row (absent, another tenant's, or already gone).
    /// </summary>
    Task<MediaAccessAsset?> FindByAssetKeyAsync(
        Guid organizationId, string assetKey, CancellationToken ct = default);

    /// <summary>
    /// The row a mint caller named by reference. <paramref name="imageRefKind"/> is one of
    /// <see cref="MediaReferenceKinds"/>; an unknown kind returns <c>null</c>.
    /// </summary>
    Task<MediaAccessAsset?> FindByReferenceAsync(
        Guid organizationId, string imageRefKind, Guid imageRefId, CancellationToken ct = default);
}

/// <summary>The reference kinds a mint caller may name (migration plan §8.2).</summary>
public static class MediaReferenceKinds
{
    /// <summary>A <c>MessageAttachment</c> row.</summary>
    public const string Attachment = "attachment";

    /// <summary>An <c>InventoryImage</c> row.</summary>
    public const string InventoryImage = "inventoryImage";
}
