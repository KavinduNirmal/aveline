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

from app.agents.customer_memory.graph import build_memory_graph
from app.agents.customer_memory.parsing import parse_message
from app.agents.visual_insight.graph import build_visual_graph
from app.core.config import get_settings
from app.gate import classify_by_rules
from app.llm.runtime import visual_llm_or_none
from app.schemas.response import AgentResponse, AgentStatus
from app.schemas.state import AgentState
from app.tools.registry import ToolRegistry
from app.workflows.checkpointer import create_checkpointer
from app.workflows.state_events import run_graph_with_states

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


async def run_memory_agent(state: ConciergeState) -> dict[str, Any]:
    """Run the Customer Memory Agent (``app/agents/customer_memory/graph.py``).

    Resolves customer context from ``org_context`` (organization_id + phone_number/customer_id).
    When a customer can be resolved the real sub-graph runs (identify -> consent -> parse ->
    retrieve -> persist -> compose). Otherwise the agent falls back to a message-level parse so
    the workflow remains testable without a backend.
    """
    org_context = state.get("org_context") or {}
    org_id = org_context.get("organization_id") or org_context.get("org_id")
    customer_id = org_context.get("customer_id")
    phone = org_context.get("phone_number")
    customer_name = org_context.get("customer_name")
    message = state.get("message", "")

    if not org_id or (not customer_id and not phone):
        # No customer context: message-level parse only (no backend calls).
        parsed = parse_message(message, intent_hint=(state.get("intent") or {}).get("intent_type"))
        return {
            "memory_output": {
                "agent": "memory",
                "ran": True,
                "status": "skipped",
                "reason": "no customer context available",
                "parsed_intent": parsed["parsed_intent"],
            }
        }

    registry = ToolRegistry()
    graph = build_memory_graph(registry)
    mem_state = {
        "org_id": str(org_id),
        "customer_id": str(customer_id) if customer_id else None,
        "phone_number": str(phone) if phone else None,
        "customer_name": customer_name,
        "message": message,
        "intent_type": (state.get("intent") or {}).get("intent_type"),
        "channel": org_context.get("channel", "whatsapp"),
        "direction": org_context.get("direction", "inbound"),
    }
    result = await graph.ainvoke(mem_state)
    output = result.get("output") or {
        "status": result.get("status") or "skipped",
        "reason": result.get("reason"),
        "extracted_memories": [],
        "detected_events": [],
    }
    return {"memory_output": {"agent": "memory", "ran": True, **output}}


async def run_visual_agent(state: ConciergeState) -> dict[str, Any]:
    """Run the Visual Insight Agent (Elle — Slice 2).

    Executes ``app/agents/visual_insight/graph.py`` sub-graph, which fills
    ``items``, ``looks``, and ``suggestion`` for styling & visual sourcing.
    """
    org_context = state.get("org_context") or {}
    org_id = str(org_context.get("organization_id") or org_context.get("org_id") or "")
    customer_id = org_context.get("customer_id")
    customer_name = org_context.get("customer_name")
    message = state.get("message", "")
    image_url = org_context.get("image_url")

    direction = org_context.get("direction")
    staff_query = org_context.get("staff_query") if "staff_query" in org_context else (not direction or direction in ("outbound", "internal"))

    registry = ToolRegistry()
    llm = visual_llm_or_none(get_settings())
    graph = build_visual_graph(registry, llm=llm)
    vis_state = {
        "org_id": org_id,
        "customer_id": str(customer_id) if customer_id else None,
        "customer_name": customer_name,
        "message": message,
        "image_url": image_url,
        "intent_type": (state.get("intent") or {}).get("intent_type"),
        "preferences": (state.get("memory_output") or {}).get("extracted_memories"),
        "channel": org_context.get("channel", "internal"),
        "direction": direction,
        "staff_query": bool(staff_query),
    }
    result = await graph.ainvoke(vis_state)
    output = result.get("output") or {
        "status": "success",
        "agent": "visual",
        "ran": True,
        "items": [],
        "looks": [],
        "suggestion": None,
    }
    return {"visual_output": {"agent": "visual", "ran": True, **output}}



def run_commerce_agent(state: ConciergeState) -> dict[str, Any]:
    """Stub Commerce Agent node.

    TODO(Slice 3): replace with the real ``app/agents/commerce/graph.py`` sub-graph, which
    fills ``summary``/``payment``/``courier`` and, when approval is required, pauses for
    human-in-the-loop sign-off. Until then this stub declares the structured output shape
    with ``status == "stub"`` and no fabricated payment or approval data.
    """
    return {
        "commerce_output": {
            "agent": "commerce",
            "ran": True,
            "status": "stub",
            "note": "Commerce validation is not wired yet (Slice 3).",
            "needs_approval": False,
            "summary": None,
            "payment": None,
            "courier": None,
        }
    }


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
    org_context = state.get("org_context") or {}
    if "visual" in agents or org_context.get("image_url"):
        return "visual_agent"
    if "commerce" in agents:
        return "commerce_agent"
    return "formulate_response"



def route_after_visual(state: dict[str, Any]) -> str:
    """Route to commerce if visual output requires commerce actions; otherwise formulate."""
    if state.get("requires_commerce"):
        return "commerce"

    visual_output = state.get("visual_output")
    if isinstance(visual_output, dict) and visual_output.get("requires_commerce"):
        return "commerce"

    return "formulate"


def _route_after_visual(state: ConciergeState) -> str:
    intent = state.get("intent") or {}
    agents = intent.get("suggested_agents", [])
    if "commerce" in agents or route_after_visual(state) == "commerce":
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
