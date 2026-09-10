"""State-emitting execution of the concierge workflow.

LangGraph's ``astream_events`` (v2) reports an ``on_chain_start`` event for every node
with ``metadata.langgraph_node`` set to the node name. This module maps each concierge
node to an :class:`AgentState` and drives the graph so the caller can publish lifecycle
states as the workflow progresses.

The mapping is a starting point and is tuned as the specialist sub-graphs land:

    intent_gate        -> thinking   (classifying / routing)
    memory_agent       -> searching  (customer memory retrieval)
    visual_agent       -> searching  (visual sourcing)
    commerce_agent     -> tool_call  (commerce tools)
    formulate_response -> processing (synthesis)
"""

import asyncio
from collections.abc import Awaitable, Callable
from typing import Any

from app.schemas.state import AgentState

#: Concierge node name -> the state to broadcast when the node starts.
NODE_STATES: dict[str, AgentState] = {
    "intent_gate": AgentState.thinking,
    "memory_agent": AgentState.searching,
    "visual_agent": AgentState.searching,
    "commerce_agent": AgentState.tool_call,
    "formulate_response": AgentState.processing,
}

#: Async callback invoked with each lifecycle state as the workflow progresses.
StateEmitter = Callable[[AgentState], Awaitable[None]]


async def run_graph_with_states(
    graph: Any,
    initial: dict[str, Any],
    config: dict[str, Any] | None,
    on_state: StateEmitter,
    checkpointer: Any | None = None,
    state_delay_ms: int = 0,
) -> dict[str, Any]:
    """Run ``graph`` via ``astream_events``, emitting a state per node start.

    Args:
        graph: The compiled LangGraph workflow.
        initial: The initial workflow state.
        config: Optional run configuration (e.g. ``{"configurable": {"thread_id": ...}}``).
        on_state: Async callback invoked with each mapped state as a node starts.
        checkpointer: Optional checkpointer to pass through to ``astream_events``.
        state_delay_ms: Optional artificial delay (ms) inserted between emitted states so
            clients can visibly animate each lifecycle state during integration testing.
            0 disables the delay.

    Returns:
        The final aggregated workflow state (from the top-level ``on_chain_end``).
    """
    final: dict[str, Any] | None = None
    kwargs: dict[str, Any] = {"config": config, "version": "v2"}
    if checkpointer is not None:
        kwargs["checkpointer"] = checkpointer
    async for event in graph.astream_events(initial, **kwargs):
        kind = event.get("event")
        if kind == "on_chain_start":
            node = (event.get("metadata") or {}).get("langgraph_node")
            # Only emit for the node function itself (name == node), not for routing
            # helpers that also report the same langgraph_node.
            if node is not None and event.get("name") == node:
                state = NODE_STATES.get(node)
                if state is not None:
                    await on_state(state)
                    if state_delay_ms > 0:
                        await asyncio.sleep(state_delay_ms / 1000)
        elif kind == "on_chain_end" and event.get("name") == "LangGraph":
            output = (event.get("data") or {}).get("output")
            if isinstance(output, dict):
                final = output
    if final is None:
        raise RuntimeError("Workflow finished without a final state.")
    return final
