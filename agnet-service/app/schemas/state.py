"""Agent lifecycle state contract shared with the API and clients.

The agent service publishes ``agent.status`` events (ADR-014) carrying one of these
states as the workflow progresses. The API resolves the ``thread_id`` to a conversation
and broadcasts the state to the Salon over SignalR so both the web and Flutter chat can
animate Aveline's blossom accordingly.

The string values are the canonical wire contract and MUST stay in sync with the C#
``AgentStateDto`` and the TS/Dart client enums.
"""

from enum import StrEnum


class AgentState(StrEnum):
    """Aveline's current agentic-workflow state, reflected by the blossom avatar."""

    idle = "idle"
    thinking = "thinking"
    searching = "searching"
    processing = "processing"
    tool_call = "tool_call"
    waiting = "waiting"
    success = "success"
    error = "error"
    response = "response"
