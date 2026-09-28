namespace Aveline.Api.Modules.Media;

/// <summary>
/// Provider-neutral metadata for a stored asset. Cloudinary maps <see cref="Labels"/> to tags
/// and <see cref="Attributes"/> to contextual metadata; a byte-store implementation may ignore
/// both. Nothing about the record names a provider (strategy §3.2, D1).
/// </summary>
/// <param name="Labels">The asset labels, e.g. <c>salon-image</c>, <c>kind:pdf</c>.</param>
/// <param name="Attributes">Free-form key/value context, e.g. <c>o={orgId}</c>.</param>
public sealed record MediaMetadata(
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, string> Attributes);
