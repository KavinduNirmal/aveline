namespace Aveline.Api.Modules.Media;

/// <summary>
/// Non-secret Cloudinary settings. The account credentials — <see cref="CloudName"/>,
/// <see cref="ApiKey"/> and <see cref="ApiSecret"/> — are deliberately <strong>not</strong>
/// bound from <c>appsettings.json</c>, which is committed; they are resolved once at startup
/// from <c>CLOUDINARY_URL</c> (or its discrete fallback) by <see cref="CloudinaryUrlParser"/>
/// and written onto this instance (strategy §3.4).
/// </summary>
public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    /// <summary>The Cloudinary cloud name; part of the credential, never committed.</summary>
    public string CloudName { get; set; } = string.Empty;

    /// <summary>The Cloudinary API key; part of the credential, never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Cloudinary API secret; part of the credential, never committed.</summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>The folder every public id is rooted under.</summary>
    public string FolderRoot { get; set; } = "aveline";

    /// <summary>The delivery type for the public catalog tier.</summary>
    public string CatalogDeliveryType { get; set; } = "upload";

    /// <summary>The delivery type for the protected conversation tier.</summary>
    public string ProtectedDeliveryType { get; set; } = "authenticated";

    /// <summary>Upload timeout, in seconds.</summary>
    public int UploadTimeoutSeconds { get; set; } = 30;

    /// <summary>The documented starting concurrency: ~10 uploads.</summary>
    public int MaxConcurrentUploads { get; set; } = 10;

    /// <summary>Retry attempts for a Cloudinary <c>420</c> or 5xx response.</summary>
    public int UploadRetryAttempts { get; set; } = 3;
}
