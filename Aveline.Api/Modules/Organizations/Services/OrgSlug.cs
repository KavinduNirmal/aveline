using System.Text.RegularExpressions;

namespace Aveline.Api.Modules.Organizations.Services;

/// <summary>
/// Normalizes a boutique name or user-supplied slug into a canonical URL slug matching
/// <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>, truncated to the 100-char <c>Organizations.Slug</c>
/// column. Lowercasing here is critical because tenant lookups (e.g. <c>by-slug</c>) match
/// case-sensitively and expect lowercase slugs.
/// </summary>
public static partial class OrgSlug
{
    /// <summary>Maximum slug length, mirroring <c>OrganizationConfiguration.Slug</c>.</summary>
    public const int MaxLength = 100;

    public static string From(string nameOrSlug)
    {
        var slug = NonAlphanumericRegex()
            .Replace((nameOrSlug ?? string.Empty).ToLowerInvariant(), "-")
            .Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].Trim('-');
        }

        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();
}
