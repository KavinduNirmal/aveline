namespace Aveline.Api.Modules.Media;

/// <summary>
/// The result of a successful <see cref="IMediaStorage.PutAsync"/>. <see cref="StorageKey"/>
/// encodes the delivery type as well as the asset identity, so a row is decidable on its own
/// (strategy §3.3).
/// </summary>
/// <param name="Provider">The provider that stored the bytes, e.g. <c>cloudinary</c>.</param>
/// <param name="StorageKey">The encoded key, e.g. <c>image/authenticated:aveline/…</c>.</param>
/// <param name="Url">The absolute delivery URL to persist on the row.</param>
public sealed record StoredMedia(string Provider, string StorageKey, string Url);
