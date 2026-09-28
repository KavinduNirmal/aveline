namespace Aveline.Api.Modules.VisualIntelligence.Constants;

public static class CatalogStatusVocabulary
{
    public const string Available = "available";
    public const string OnHold = "on_hold";
    public const string Unavailable = "unavailable";
    public const string SoldOut = "sold_out";
    public const string Archived = "archived";
    public const string Reserved = "reserved";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Available,
        OnHold,
        Unavailable,
        SoldOut,
        Archived,
        Reserved
    };

    public static bool IsValid(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) && All.Contains(status.Trim());
    }
}
