namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// Canonical agent persona keys used to attribute a <see cref="Message"/> to an Aveline
/// agent. <see cref="Aveline"/> is the orchestrator (a first-class sender, distinct from
/// the specialists); Ava (memory), Elle (visual), and Lina (commerce) are the specialists.
/// </summary>
public static class AgentKeys
{
    /// <summary>The orchestrator / concierge persona.</summary>
    public const string Aveline = "aveline";

    /// <summary>Customer memory specialist.</summary>
    public const string Ava = "ava";

    /// <summary>Visual insight / sourcing specialist.</summary>
    public const string Elle = "elle";

    /// <summary>Commerce specialist.</summary>
    public const string Lina = "lina";
}
