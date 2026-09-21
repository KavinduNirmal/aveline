namespace Aveline.Api.Modules.Media;

/// <summary>
/// One write through the provider seam. <see cref="Metadata"/> is optional so a caller with
/// nothing to say cannot be forced to invent labels (strategy §3.2, verbatim shape).
/// </summary>
/// <param name="PublicId">Provider-agnostic key; the Cloudinary adapter prefixes the folder root.</param>
/// <param name="Bytes">The bytes to store.</param>
/// <param name="ContentType">The normalised content type, from the allow-list.</param>
/// <param name="FileName">The original file name; never enters provider context.</param>
/// <param name="Tier">Who may read the asset.</param>
/// <param name="Metadata">Optional provider-neutral labels and attributes.</param>
public sealed record MediaPutRequest(
    string PublicId,
    byte[] Bytes,
    string ContentType,
    string FileName,
    MediaTier Tier,
    MediaMetadata? Metadata = null);   // optional: a caller that has none is not a caller bug
