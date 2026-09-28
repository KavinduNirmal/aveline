namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// What a human did to a <see cref="MessageKind.SignOff"/> message.
/// </summary>
/// <remarks>
/// The decision log is immutable and append-only. A revocation does not mutate the approval
/// row: it appends a <see cref="Revoked"/> row, so the history of who approved what, and when
/// it was taken back, is preserved. The newest row is the authoritative state.
/// </remarks>
public enum SignOffDecisionKind
{
    /// <summary>The associate released the SignOff.</summary>
    Approved,

    /// <summary>The associate dropped it.</summary>
    Rejected,

    /// <summary>A supervisor took an approval back; the SignOff is decidable again.</summary>
    Revoked,
}
