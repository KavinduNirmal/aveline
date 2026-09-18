namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// The render type of a <see cref="Message"/>. The kind selects which rich block
/// renderer the client uses. Quiet-luxury vocabulary: Note, Look, Piece, AtAGlance,
/// ClientMessage, SignOff, Payment, Courier, Suggestion.
/// </summary>
public enum MessageKind
{
    /// <summary>Plain text.</summary>
    Note,

    /// <summary>Image / moodboard / visual reference.</summary>
    Look,

    /// <summary>A curated inventory item card.</summary>
    Piece,

    /// <summary>Tabular comparison (options, sizes, prices).</summary>
    AtAGlance,

    /// <summary>Inbound WhatsApp/Instagram content forwarded from the customer.</summary>
    ClientMessage,

    /// <summary>Human-in-the-loop approval request.</summary>
    SignOff,

    /// <summary>Payment link / invoice.</summary>
    Payment,

    /// <summary>Delivery update.</summary>
    Courier,

    /// <summary>A soft, proactive recommendation.</summary>
    Suggestion,
}
