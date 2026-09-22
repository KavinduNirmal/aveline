namespace Aveline.Api.Modules.Media;

/// <summary>
/// Who may read a stored asset. The tier is about access, not format: a PDF is
/// <see cref="Protected"/> with a <c>kind:pdf</c> label, and the enum gains no third value
/// (strategy §3.2).
/// </summary>
public enum MediaTier
{
    /// <summary>Anyone may read it: an absolute, unsigned CDN URL is the grant.</summary>
    Public,

    /// <summary>Only an authenticated reader, or a holder of a minted token, may read it.</summary>
    Protected,
}
