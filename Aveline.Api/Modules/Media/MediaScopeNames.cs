namespace Aveline.Api.Modules.Media;

/// <summary>
/// The wire names of the three scopes (migration plan §7.4). The token carries these strings, so
/// they are part of the frozen format: <c>asset.public</c>, <c>attachment.view</c>,
/// <c>vision.analyze</c>.
/// </summary>
public static class MediaScopeNames
{
    /// <summary>The wire name written into the token's <c>s</c> claim.</summary>
    public static string WireName(MediaScope scope) => scope switch
    {
        MediaScope.AssetPublic => "asset.public",
        MediaScope.AttachmentView => "attachment.view",
        MediaScope.VisionAnalyze => "vision.analyze",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown media scope."),
    };

    /// <summary>Reads a wire name back; <c>null</c> when it is not one of the three.</summary>
    public static MediaScope? TryParse(string? value) => value switch
    {
        "asset.public" => MediaScope.AssetPublic,
        "attachment.view" => MediaScope.AttachmentView,
        "vision.analyze" => MediaScope.VisionAnalyze,
        _ => null,
    };
}
