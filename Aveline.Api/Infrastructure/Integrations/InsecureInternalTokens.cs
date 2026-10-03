namespace Aveline.Api.Infrastructure.Integrations;

/// <summary>
/// Recognises internal-service tokens that are known development defaults or otherwise too weak
/// to be a credential. The internal token grants <c>role=InternalService</c> and
/// <c>scope=internal:all</c> over every <c>/internal/*</c> group, so a guessable value is a
/// full-repository-access bug rather than an inconvenience.
///
/// <para>
/// The previous check compared against a single literal (<c>change-me-internal-token</c>). That
/// left the token advertised in <c>docker-compose.yml</c>
/// (<c>aveline-local-development-secret-token-2026</c>) acceptable, so a deployment that omitted
/// <c>INTERNAL_API_TOKEN</c> silently inherited a value published in the repository. Verified
/// live on 2026-10-03 that the deployed value was <i>not</i> the default, but the guard was one
/// forgotten environment variable away from being exploitable (security assessment F-2.6).
/// </para>
///
/// <para>
/// Two layers use this: the authentication handler refuses to authenticate against a weak
/// configured token, and <see cref="InternalTokenSecurityGuard"/> refuses to boot Production
/// with one.
/// </para>
/// </summary>
public static class InsecureInternalTokens
{
    /// <summary>Minimum plausible length of a real shared secret.</summary>
    public const int MinimumLength = 32;

    /// <summary>
    /// Exact values known to be published in this repository or its documentation. Matching is
    /// case-insensitive and ignores surrounding whitespace.
    /// </summary>
    public static readonly string[] KnownPlaceholders =
    [
        "change-me-internal-token",
        "aveline-local-development-secret-token-2026",
        "aveline-local-development-secret-token",
        "local-development-secret-token",
        "change-me",
        "changeme",
        "internal-token",
        "secret",
        "password",
        "test",
    ];

    /// <summary>
    /// Substrings that mark a value as development-shaped regardless of its exact spelling.
    /// </summary>
    public static readonly string[] InsecureFragments =
    [
        "change-me",
        "changeme",
        "local-development",
        "development-secret",
        "example",
        "placeholder",
        "your-token",
        "todo",
    ];

    /// <summary>
    /// Returns a reason when <paramref name="token"/> is a value published in this repository or
    /// otherwise obviously development-shaped, or <c>null</c> when it is not. This is the check the
    /// authentication handler applies, so a test or development host using a short throwaway token
    /// keeps working - only a *known guessable* value is refused everywhere.
    /// </summary>
    public static string? RejectKnownPlaceholder(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "it is missing or blank";
        }

        var candidate = token.Trim();

        foreach (var placeholder in KnownPlaceholders)
        {
            if (string.Equals(candidate, placeholder, StringComparison.OrdinalIgnoreCase))
            {
                return $"it is the known development placeholder '{placeholder}'";
            }
        }

        foreach (var fragment in InsecureFragments)
        {
            if (candidate.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return $"it contains the development marker '{fragment}'";
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a reason when <paramref name="token"/> is unfit to protect Production, or
    /// <c>null</c> when it is acceptable. Applies the weakness rules (length, entropy) on top of
    /// <see cref="RejectKnownPlaceholder"/>.
    ///
    /// <para>
    /// The Production startup guard uses this; the authentication handler uses only the placeholder
    /// check. Splitting them matters: a test host legitimately configures a short token such as
    /// <c>test-internal-analyze-key</c>, and failing that closed would break the suite without
    /// improving production security. Production still gets the full strength requirement, enforced
    /// once at boot.
    /// </para>
    /// </summary>
    public static string? Reject(string? token)
    {
        if (RejectKnownPlaceholder(token) is { } known)
        {
            return known;
        }

        var candidate = token!.Trim();

        if (candidate.Length < MinimumLength)
        {
            return $"it is only {candidate.Length} characters; use at least {MinimumLength} random bytes";
        }

        // A single repeated character ("aaaa…") is long but carries no entropy.
        if (candidate.Distinct().Count() < 8)
        {
            return "it uses fewer than 8 distinct characters, so it carries almost no entropy";
        }

        return null;
    }

    /// <summary>Convenience predicate over <see cref="Reject"/> (the Production bar).</summary>
    public static bool IsInsecure(string? token) => Reject(token) is not null;
}
