namespace Aveline.Api.Modules.Media;

/// <summary>
/// Where image bytes live, selected by <c>Media:Provider</c>. The value is written to every row
/// a storage implementation creates, so a mixed population is decidable from the row alone.
/// </summary>
public enum MediaProvider
{
    /// <summary>Bytes live in the database (<c>bytea</c>): tests and the rollback path.</summary>
    Database,

    /// <summary>Bytes live in Cloudinary; the database keeps only the reference.</summary>
    Cloudinary,
}
