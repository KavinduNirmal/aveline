"""Top-level concierge orchestrator.

Coordinates the three agents for an inbound customer message:

    intent_gate -> memory_agent -> visual_agent -> commerce_agent -> formulate_response

The Intent Gate classifies and routes the message. Out-of-scope input
short-circuits straight to ``formulate_response``. The agent nodes are
**placeholder passthroughs** that record participation; slice owners replace them
with their real sub-graphs (``app/agents/<name>/graph.py``).

Workflow state is checkpointed (ADR-002) so an agent can pause for human approval
and resume exactly where it left off. ``run_concierge`` uses the Postgres
checkpointer when a ``thread_id`` is supplied.
"""

import logging
from collections.abc import Awaitable, Callable
from typing import Any, TypedDict

from langgraph.graph import END, START, StateGraph

from app.core.config import get_settings
from app.gate import classify_by_rules
from app.schemas.response import AgentResponse, AgentStatus
from app.schemas.state import AgentState
from app.workflows.checkpointer import create_checkpointer
from app.workflows.state_events import run_graph_with_states
from app.gate import classify_by_rules
from app.schemas.response import AgentResponse, AgentStatus
from app.workflows.checkpointer import create_checkpointer

logger = logging.getLogger("aveline.agent.concierge")


class ConciergeState(TypedDict, total=False):
    """State flowing through the concierge workflow."""

    message: str
    org_context: dict[str, Any]
    intent: dict[str, Any] | None
    memory_output: dict[str, Any] | None
    visual_output: dict[str, Any] | None
    commerce_output: dict[str, Any] | None
    response: dict[str, Any] | None


# ---------------------------------------------------------------------------
# Nodes
# ---------------------------------------------------------------------------


def run_intent_gate(state: ConciergeState) -> dict[str, Any]:
    """Classify the message and decide routing.

    The intent is stored as a plain dict so the state stays JSON-serializable
    for checkpointing (ADR-002).
    """
    intent = classify_by_rules(state["message"])
    logger.debug("Intent gate classified message as %s.", intent.intent_type)
    return {"intent": intent.model_dump()}


def run_memory_agent(state: ConciergeState) -> dict[str, Any]:
    """Placeholder Customer Memory Agent node.

    TODO(Slice 1): replace with the real ``app/agents/customer_memory/graph.py``
    sub-graph. For now it records participation so the workflow is testable.
    """
    return {"memory_output": {"agent": "memory", "ran": True}}


def run_visual_agent(state: ConciergeState) -> dict[str, Any]:
    """Placeholder Visual Insight Agent node.

    TODO(Slice 2): replace with the real ``app/agents/visual_insight/graph.py``
    sub-graph.
    """
    return {"visual_output": {"agent": "visual", "ran": True}}


def run_commerce_agent(state: ConciergeState) -> dict[str, Any]:
    """Placeholder Commerce Agent node.

    TODO(Slice 3): replace with the real ``app/agents/commerce/graph.py``
    sub-graph, which may interrupt for human approval.
    """
    return {"commerce_output": {"agent": "commerce", "ran": True}}


def formulate_response(state: ConciergeState) -> dict[str, Any]:
    """Build the final ``AgentResponse`` envelope from the collected outputs.

    The response is stored as a plain dict so the state stays JSON-serializable
    for checkpointing (ADR-002).
    """
    intent = state.get("intent") or {}
    if intent.get("intent_type") == "out_of_scope":
        response = AgentResponse(
            status=AgentStatus.out_of_scope,
            output={"reason": "Request is outside the boutique domain."},
        )
    else:
        response = AgentResponse(
            status=AgentStatus.success,
            output={
                "intent": intent.get("intent_type", "general_inquiry"),
                "memory": state.get("memory_output"),
                "visual": state.get("visual_output"),
                "commerce": state.get("commerce_output"),
            },
        )
    return {"response": response.model_dump()}


# ---------------------------------------------------------------------------
# Routing
# ---------------------------------------------------------------------------


def _route_after_intent(state: ConciergeState) -> str:
    intent = state.get("intent") or {}
    if intent.get("is_relevant", True) is False:
        return "formulate_response"
    return "memory_agent"


def _route_after_memory(state: ConciergeState) -> str:
    intent = state.get("intent") or {}
    agents = intent.get("suggested_agents", [])
    if "visual" in agents:
        return "visual_agent"
    if "commerce" in agents:
        return "commerce_agent"
    return "formulate_response"


def _route_after_visual(state: ConciergeState) -> str:
    intent = state.get("intent") or {}
    agents = intent.get("suggested_agents", [])
    if "commerce" in agents:
        return "commerce_agent"
    return "formulate_response"


# ---------------------------------------------------------------------------
# Graph construction
# ---------------------------------------------------------------------------


def build_concierge_graph():
    """Build and compile the concierge workflow graph (no checkpointer)."""
    graph = StateGraph(ConciergeState)

    graph.add_node("intent_gate", run_intent_gate)
    graph.add_node("memory_agent", run_memory_agent)
    graph.add_node("visual_agent", run_visual_agent)
    graph.add_node("commerce_agent", run_commerce_agent)
    graph.add_node("formulate_response", formulate_response)

    graph.add_edge(START, "intent_gate")
    graph.add_conditional_edges("intent_gate", _route_after_intent)
    graph.add_conditional_edges("memory_agent", _route_after_memory)
    graph.add_conditional_edges("visual_agent", _route_after_visual)
    graph.add_edge("commerce_agent", "formulate_response")
    graph.add_edge("formulate_response", END)

    return graph.compile()


async def run_concierge(
    message: str,
    org_context: dict[str, Any] | None = None,
    thread_id: str | None = None,
    on_state: Callable[[AgentState], Awaitable[None]] | None = None,
) -> AgentResponse:
    """Run the concierge workflow for ``message`` and return the final response.

    Args:
        message: The inbound customer message.
        org_context: Optional organization context (reserved for prompt injection).
        thread_id: Optional checkpoint thread id. When supplied the workflow is
            checkpointed to Postgres so it can pause/resume (ADR-002).
        on_state: Optional async callback invoked with each lifecycle state as the
            workflow progresses (e.g. to publish ``agent.status`` events). When omitted
            the workflow runs with plain ``ainvoke`` and no state events are emitted.

    Returns:
        The final ``AgentResponse`` envelope.
    """
    graph = build_concierge_graph()
    initial: dict[str, Any] = {
        "message": message,
        "org_context": org_context or {},
        "intent": None,
        "memory_output": None,
        "visual_output": None,
        "commerce_output": None,
        "response": None,
    }

    if on_state is not None:
        # Stream node boundaries so the caller can publish lifecycle states.
        config = {"configurable": {"thread_id": thread_id}} if thread_id is not None else None
        state_delay_ms = get_settings().agent_state_delay_ms
        if thread_id is not None:
            async with create_checkpointer() as checkpointer:
                result = await run_graph_with_states(
                    graph,
                    initial,
                    config,
                    on_state,
                    checkpointer=checkpointer,
                    state_delay_ms=state_delay_ms,
                )
        else:
            result = await run_graph_with_states(
                graph,
                initial,
                config,
                on_state,
                state_delay_ms=state_delay_ms,
            )
    elif thread_id is not None:
        async with create_checkpointer() as checkpointer:
            result = await graph.ainvoke(
                initial,
                config={"configurable": {"thread_id": thread_id}},
                checkpointer=checkpointer,
            )
    else:
        result = await graph.ainvoke(initial)

    return AgentResponse.model_validate(result["response"])
