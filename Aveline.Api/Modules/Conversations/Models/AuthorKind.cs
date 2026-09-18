namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// The kind of author of a <see cref="Message"/>. Boutique customers are external and
/// never appear as a sender; their inbound content surfaces as a <see cref="MessageKind.ClientMessage"/>
/// card authored by <see cref="System"/>.
/// </summary>
public enum AuthorKind
{
    /// <summary>A boutique staff member or owner.</summary>
    User,

    /// <summary>An Aveline agent persona (see <see cref="AgentKeys"/>).</summary>
    Agent,

    /// <summary>The platform itself (e.g. inbound channel forwarding).</summary>
    System,
}
