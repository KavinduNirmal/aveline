using System.Text;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The versioned first-contact disclosure (plan §4.3). It carries the four required elements:
///
/// <list type="number">
///   <item>the boutique's display name, so the customer knows who is messaging;</item>
///   <item>the AI disclosure <i>with</i> the human-agent line, so the customer knows both that an
///     assistant is answering and that a person reads the conversation and can take over;</item>
///   <item>the data-policy URL, so "what is stored and why" is one tap away;</item>
///   <item>the signed permanent opt-out URL, so the customer can leave without waiting for a human.</item>
/// </list>
///
/// plus the <c>Reply STOP</c> / <c>HELP</c> line.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not the staff-facing salon welcome.</b> <c>ConversationService</c> has a "Welcome to
/// your Salon" string for boutique staff in the console, and it mentions the Ava/Elle/Lina personas.
/// That is a different message for a different audience; reusing it here would tell a customer about
/// internal tooling and promise features that do not apply to their own thread. The two are kept
/// separate deliberately (plan §4.3).
/// </para>
/// <para>
/// <b>The length matters, because this travels as an image caption.</b> Meta caps an image caption at
/// 1024 characters while a text body gets 4096, so the boutique's name appearing three times is
/// deliberate weight rather than carelessness. <c>DisclosureDispatchService</c> checks the length
/// before choosing the caption shape and sends text instead when it would not fit.
/// </para>
/// </remarks>
public sealed class DisclosureBodyBuilder : IDisclosureBodyBuilder
{
    /// <summary>The disclosure version. Bump it whenever the copy below changes.</summary>
    public const string CurrentVersionValue = "v2";

    /// <inheritdoc />
    public string CurrentVersion => CurrentVersionValue;

    /// <inheritdoc />
    public DisclosureBody Build(
        string boutiqueDisplayName, string avelineUrl, string dataPolicyUrl, string optOutUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boutiqueDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(avelineUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPolicyUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(optOutUrl);

        var text = new StringBuilder()
            .Append("Hello! Thanks for reaching out to ").Append(boutiqueDisplayName)
            .Append(". ").Append(boutiqueDisplayName)
            .Append(" uses Aveline, an AI assistant built for our in-house tasks. ")
            .Append("You can learn more about Aveline here: ").Append(avelineUrl)
            .Append('.')
            .Append('\n')
            .Append('\n')
            .Append("Aveline helps behind the scenes, but a real member of our team reads every ")
            .Append("conversation and can step in at any time. Only our team and Aveline, acting on ")
            .Append("our behalf, will contact you, and only approved messages will be sent.")
            .Append('\n')
            .Append('\n')
            .Append("What Aveline stores on behalf of ").Append(boutiqueDisplayName)
            .Append(" and why: ").Append(dataPolicyUrl)
            .Append('\n')
            .Append("Opt out permanently at any time: ").Append(optOutUrl)
            .Append('\n')
            .Append('\n')
            .Append("Reply STOP to opt out, or HELP for a human.")
            .ToString();

        return new DisclosureBody(text, CurrentVersionValue);
    }
}
