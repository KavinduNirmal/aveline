namespace Aveline.Api.Modules.CustomerConcierge.Common;

/// <summary>
/// The retrieval search modes a customer-memory query may name, with the same vocabulary the
/// handbook search uses (ADR-025).
/// </summary>
/// <remarks>
/// <para>
/// <c>hybrid</c> (default) fuses the dense and lexical legs; <c>lexical</c> and <c>vector</c> run a
/// single leg. The single-leg modes exist so retrieval quality can be reported per leg; the agent
/// always asks for the default.
/// </para>
/// <para>
/// One definition for the vocabulary. An unknown mode is rejected rather than silently treated as
/// hybrid: a caller that misspells <c>lexical</c> and receives hybrid results would read a fused
/// answer as a single-leg measurement, which is exactly the confusion the eval modes exist to
/// remove.
/// </para>
/// </remarks>
public static class MemorySearchModes
{
    public const string Hybrid = "hybrid";
    public const string Lexical = "lexical";
    public const string Vector = "vector";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Hybrid, Lexical, Vector,
    };

    /// <summary>
    /// Whether <paramref name="mode"/> is one this store can run. An absent or whitespace value is
    /// valid and means <see cref="Hybrid"/>.
    /// </summary>
    public static bool IsValid(string? mode)
        => string.IsNullOrWhiteSpace(mode) || Allowed.Contains(mode.Trim());

    /// <summary>
    /// The canonical (lower-case) mode for <paramref name="mode"/>, defaulting an absent value to
    /// <see cref="Hybrid"/> and throwing for a value this store cannot run.
    /// </summary>
    /// <exception cref="ArgumentException">The mode is not one of hybrid, lexical or vector.</exception>
    public static string Normalise(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return Hybrid;
        }

        var trimmed = mode.Trim();
        if (Allowed.Contains(trimmed))
        {
            return trimmed.ToLowerInvariant();
        }

        throw new ArgumentException(
            $"Unknown search mode '{mode}'. Use '{Hybrid}', '{Lexical}' or '{Vector}'.", nameof(mode));
    }
}
