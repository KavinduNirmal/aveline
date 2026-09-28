namespace Aveline.Api.Modules.Media;

/// <summary>
/// What a minted media token may be used for. Scope is bound into the token so a leaked token
/// for one workflow cannot read an unrelated asset (migration plan §7.5). The wire names are
/// <c>asset.public</c>, <c>attachment.view</c> and <c>vision.analyze</c>.
/// </summary>
public enum MediaScope
{
    /// <summary>Catalog imagery. Not tokenised in practice; the CDN URL is public (F-7/Q4).</summary>
    AssetPublic,

    /// <summary>A conversation attachment read by an authenticated org member.</summary>
    AttachmentView,

    /// <summary>One analysis by the vision provider; single-use, so a replay has no use.</summary>
    VisionAnalyze,
}
