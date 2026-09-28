namespace Aveline.Api.Modules.Media;

/// <summary>
/// What a read-back of one stored asset reports: its tags and the derived formats the account has
/// materialised for it. Used by the live suite to prove <c>overwrite=false</c> preserved the tags
/// (risk R5) and to observe the real <c>f_auto</c> derivation count (strategy §3.7, §R8).
/// </summary>
/// <param name="Tags">The asset's tags, as the provider stores them.</param>
/// <param name="DerivedFormats">The derived formats, one entry per materialised derivative.</param>
internal sealed record CloudinaryResourceInfo(
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> DerivedFormats);
