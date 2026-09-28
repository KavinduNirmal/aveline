namespace Aveline.Api.Modules.Media;

/// <summary>
/// Where a salon attachment came from — one of exactly three provenances (salon plan §6.2,
/// strategy §3.2): a staff upload through the Salon (<see cref="Web"/>), inbound channel media
/// (<see cref="WhatsApp"/>), or an image fetched from a pasted URL (<see cref="Url"/>).
/// </summary>
/// <remarks>
/// Deliberately a sealed record with a private constructor rather than an <c>enum</c>: an enum
/// can be handed an invalid value with a cast (<c>(MediaSource)42</c>), while this type can only
/// ever be one of the three instances below, so an invalid source cannot be constructed. The
/// value it carries is the exact wire token that appears in the <c>source:{value}</c> label and
/// the <c>s=</c> context pair, so there is one spelling of each source in the system.
/// </remarks>
public sealed record MediaSource
{
    private MediaSource(string value) => Value = value;

    /// <summary>The wire token: <c>web</c>, <c>whatsapp</c>, or <c>url</c>.</summary>
    public string Value { get; }

    /// <summary>A staff upload through the Salon.</summary>
    public static MediaSource Web { get; } = new("web");

    /// <summary>Inbound channel media, fetched server-side from the provider's media id.</summary>
    public static MediaSource WhatsApp { get; } = new("whatsapp");

    /// <summary>Fetched from a pasted URL (the URL-fetch path).</summary>
    public static MediaSource Url { get; } = new("url");

    /// <summary>Every valid value, in documentation order.</summary>
    public static IReadOnlyList<MediaSource> All { get; } = [Web, WhatsApp, Url];

    /// <inheritdoc />
    public override string ToString() => Value;
}
