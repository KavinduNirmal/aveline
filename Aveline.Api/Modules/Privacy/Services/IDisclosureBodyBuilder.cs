namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The first-contact disclosure body and the disclosure version it belongs to (plan §4.3). The
/// version is stamped onto <c>CustomerConsent.DisclosureVersion</c> when the message is sent, so
/// "what exactly was this customer shown?" is answerable after the copy changes.
/// </summary>
/// <param name="Text">The exact message body handed to the channel.</param>
/// <param name="Version">The disclosure version, e.g. <c>v1</c>.</param>
public sealed record DisclosureBody(string Text, string Version);

/// <summary>
/// Builds the first-contact disclosure. Pure and deterministic: it takes the already-resolved
/// identifiers and produces the words, so a snapshot test can freeze the copy without a database or
/// a host.
/// </summary>
public interface IDisclosureBodyBuilder
{
    /// <summary>The version this builder produces, e.g. <c>v1</c>.</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Builds the disclosure for one boutique.
    /// </summary>
    /// <param name="boutiqueDisplayName">The boutique's own name, as the customer knows it.</param>
    /// <param name="dataPolicyUrl">The data-policy URL, including the org query parameter.</param>
    /// <param name="optOutUrl">The signed permanent opt-out URL.</param>
    DisclosureBody Build(string boutiqueDisplayName, string dataPolicyUrl, string optOutUrl);
}
