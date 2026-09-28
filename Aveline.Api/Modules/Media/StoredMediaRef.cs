namespace Aveline.Api.Modules.Media;

/// <summary>
/// Provider, key and content type, parsed from the row. The provider is carried so a row written
/// by one adapter stays readable after the configuration changes (strategy §3.3).
/// </summary>
/// <param name="Provider">The provider that wrote the row.</param>
/// <param name="StorageKey">The encoded key the provider can resolve.</param>
/// <param name="ContentType">The stored content type, already normalised.</param>
public sealed record StoredMediaRef(string Provider, string StorageKey, string ContentType);
