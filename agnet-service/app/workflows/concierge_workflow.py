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
from app.core.config import get_settings
from app.customer_resolution import resolve_customer
from app.gate import classify_by_rules
from app.llm.runtime import memory_llm_or_none
from app.schemas.response import AgentMetadata, AgentResponse, AgentStatus
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
    # Shared customer resolution (Issue #161): a serialized CustomerResolution.
    resolution: dict[str, Any] | None
    memory_output: dict[str, Any] | None
    visual_output: dict[str, Any] | None
    commerce_output: dict[str, Any] | None
    # LLM token usage captured by the memory agent when the LLM generated the draft.
    usage: dict[str, Any] | None
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


async def run_resolve_customer(state: ConciergeState) -> dict[str, Any]:
    """Resolve the customer once so every specialist can consume it (Issue #161).

    Uses explicit context (``customer_id``/``phone_number`` in ``org_context``) when present,
    otherwise derives a phone/name from the raw message via the shared resolver. The result is
    stored as a serialized ``CustomerResolution`` in state; ambiguous/not-found results
    short-circuit to a clarification before any specialist runs.
    """
    org_context = state.get("org_context") or {}
    org_id = org_context.get("organization_id") or org_context.get("org_id")
    if not org_id:
        return {"resolution": None}

    resolution = await resolve_customer(
        str(org_id),
        state.get("message", ""),
        registry=ToolRegistry(),
        customer_id=org_context.get("customer_id"),
        phone=org_context.get("phone_number"),
    )
    logger.debug("Customer resolution: %s.", resolution.kind)
    return {"resolution": resolution.model_dump()}


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

    # The orchestrator's resolve node may have already resolved a customer from the message.
    resolution = state.get("resolution") or {}
    if resolution.get("kind") == "resolved":
        customer_id = customer_id or resolution.get("customer_id")
        profile = resolution.get("profile") or {}
        customer_name = customer_name or profile.get("fullName")

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
    llm = memory_llm_or_none(get_settings())
    graph = build_memory_graph(registry, llm=llm, org_context=org_context)
    mem_state = {
        "org_id": str(org_id),
        "customer_id": str(customer_id) if customer_id else None,
        "phone_number": str(phone) if phone else None,
        "customer_name": customer_name,
        "profile": profile if resolution.get("kind") == "resolved" else None,
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
    # Surface LLM token usage (when the draft was LLM-generated) for billing (ADR-010).
    usage = result.get("usage")
    return {"memory_output": {"agent": "memory", "ran": True, **output}, "usage": usage}


def run_visual_agent(state: ConciergeState) -> dict[str, Any]:
    """Stub Visual Insight Agent node.

    TODO(Slice 2): replace with the real ``app/agents/visual_insight/graph.py``
    sub-graph, which fills ``items``/``looks``/``suggestion`` and sets ``status``
    to ``success``/``pending``. Until then this stub declares the structured output
    shape with ``status == "stub"`` and no content, so the Salon sees no fabricated
    product data.
    """
    return {
        "visual_output": {
            "agent": "visual",
            "ran": True,
            "status": "stub",
            "note": "Visual sourcing and outfit composition are not wired yet (Slice 2).",
            "items": [],
            "looks": [],
            "suggestion": None,
        }
    }


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

    Usage is reported on every completed run (ADR-010): when the memory agent used an LLM the
    captured token split is attached; otherwise a ``rule-based`` sentinel with zero tokens is
    attached so the caller always reports a run. Blossom units are decided server-side.

    The response is stored as a plain dict so the state stays JSON-serializable
    for checkpointing (ADR-002).
    """
    metadata = _build_usage_metadata(state.get("usage"))
    intent = state.get("intent") or {}
    if intent.get("intent_type") == "out_of_scope":
        response = AgentResponse(
            status=AgentStatus.out_of_scope,
            output={"reason": "Request is outside the boutique domain."},
            metadata=metadata,
        )
    else:
        resolution = state.get("resolution") or {}
        output: dict[str, Any] = {
            "intent": intent.get("intent_type", "general_inquiry"),
        }
        if resolution.get("kind") in ("ambiguous", "not_found"):
            # No specialist produced content; the clarification is rendered by Aveline.
            output["clarification"] = resolution
        else:
            output.update(
                memory=state.get("memory_output"),
                visual=state.get("visual_output"),
                commerce=state.get("commerce_output"),
            )
        response = AgentResponse(
            status=AgentStatus.success,
            output=output,
            metadata=metadata,
        )
    return {"response": response.model_dump()}


def _build_usage_metadata(usage: dict[str, Any] | None) -> AgentMetadata:
    """Map captured LLM token usage (or its absence) onto response usage metadata.

    Args:
        usage: ``{input_tokens, output_tokens}`` captured when the memory agent drafted with an
            LLM, else ``None`` (rule-based run).

    Returns:
        An ``AgentMetadata`` carrying the model and token split, or the ``rule-based`` sentinel.
    """
    settings = get_settings()
    if usage:
        input_tokens = int(usage.get("input_tokens") or 0)
        output_tokens = int(usage.get("output_tokens") or 0)
        return AgentMetadata(
            model=settings.llm_model or "rule-based",
            tokens_used=input_tokens + output_tokens,
            input_tokens=input_tokens,
            output_tokens=output_tokens,
        )
    return AgentMetadata(model="rule-based", tokens_used=0, input_tokens=0, output_tokens=0)


# ---------------------------------------------------------------------------
# Routing
# ---------------------------------------------------------------------------


def _route_after_intent(state: ConciergeState) -> str:
    intent = state.get("intent") or {}
    if intent.get("is_relevant", True) is False:
        return "formulate_response"
    return "resolve_customer"


def _route_after_resolve(state: ConciergeState) -> str:
    # Ambiguous/not-found resolution short-circuits to a clarification before any specialist.
    resolution = state.get("resolution") or {}
    if resolution.get("kind") in ("ambiguous", "not_found"):
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
    graph.add_node("resolve_customer", run_resolve_customer)
    graph.add_node("memory_agent", run_memory_agent)
    graph.add_node("visual_agent", run_visual_agent)
    graph.add_node("commerce_agent", run_commerce_agent)
    graph.add_node("formulate_response", formulate_response)

    graph.add_edge(START, "intent_gate")
    graph.add_conditional_edges("intent_gate", _route_after_intent)
    graph.add_conditional_edges("resolve_customer", _route_after_resolve)
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
        "resolution": None,
        "memory_output": None,
        "visual_output": None,
        "commerce_output": None,
        "usage": None,
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
