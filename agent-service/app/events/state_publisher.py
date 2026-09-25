"""Agent lifecycle state publishing for the Salon (ADR-014).

The concierge workflow emits ``agent.status`` events as it progresses so the API can
broadcast Aveline's current state to connected web + Flutter clients over SignalR. Each
event carries the ``thread_id`` (the conversation anchor) and a state from
:class:`app.schemas.state.AgentState`.

The API consumes these events, resolves the ``thread_id`` to a conversation, and pushes
``ReceiveAgentState`` to the ``salon:{conversationId}`` group.
"""

import logging
from typing import Any
from uuid import UUID

from app.schemas.state import AgentState

logger = logging.getLogger("aveline.agent.events")

AGENT_STATUS = "agent.status"


async def publish_agent_state(
    bus: Any,
    org_id: UUID,
    thread_id: str | None,
    state: AgentState,
    agent_key: str | None = None,
    trace_id: UUID | None = None,
) -> None:
    """Publish a single ``agent.status`` event for the given thread.

    Args:
        bus: The event bus (duck-typed ``publish``).
        org_id: The organization the conversation belongs to.
        thread_id: The LangGraph checkpoint thread id (the conversation anchor).
        state: The agent lifecycle state to broadcast.
        agent_key: Optional persona key (``aveline``, ``ava``, ...) when the state is
            attributed to a specific agent.
        trace_id: Optional correlation id for the workflow run.
    """
    payload: dict[str, Any] = {"thread_id": thread_id, "state": state.value}
    if agent_key is not None:
        payload["agent_key"] = agent_key
    await bus.publish(AGENT_STATUS, org_id, payload, trace_id=trace_id)
    logger.debug("Published agent.status=%s for thread %s.", state.value, thread_id)
