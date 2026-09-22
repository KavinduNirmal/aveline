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

import functools
import inspect
import logging
import time
from collections.abc import Awaitable, Callable
from typing import Any, TypedDict

from langgraph.graph import END, START, StateGraph

from app.agents.commerce.graph import build_commerce_graph
from app.agents.customer_memory.graph import build_memory_graph
from app.agents.customer_memory.parsing import parse_message
from app.agents.visual_insight.graph import build_visual_graph
from app.agents.visual_insight.routing import route_after_visual
from app.context import compact
from app.core.config import get_settings
from app.customer_resolution import resolve_customer
from app.gate import classify_by_rules, supervise
from app.llm.runtime import (
    memory_llm_or_none,
    supervisor_llm_or_none,
    visual_llm_or_none,
    workflow_llm_or_none,
)
from app.observability.metrics import get_agent_metrics
from app.observability.tracing import chain_of_thought_span
from app.schemas.response import AgentMetadata, AgentResponse, AgentStatus
from app.schemas.state import AgentState
from app.telemetry.agent_telemetry import (
    NODE_STEP_KIND,
    TelemetryCollector,
    use_telemetry_collector,
)
from app.tools.registry import ToolRegistry
from app.workflows.checkpointer import create_checkpointer
from app.workflows.state_events import run_graph_with_states

logger = logging.getLogger("aveline.agent.concierge")

#: The bounded ``workflow`` metric-label value. Never the workflow/run id (plan §7.3).
WORKFLOW_NAME = "concierge"


class ConciergeState(TypedDict, total=False):
    """State flowing through the concierge workflow."""

    message: str
    org_context: dict[str, Any]
    # Conversation context (ADR-023, W1). `history` is the bounded transcript window, newest
    # last; `thread_summary` is what fell outside it; `pinned_slots` is the working set that must
    # survive trimming. All three are additive: a run without a conversation id leaves them empty.
    history: list[dict[str, Any]]
    thread_summary: str | None
    pinned_slots: dict[str, str]
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


async def run_load_context(state: ConciergeState) -> dict[str, Any]:
    """Load the conversation's transcript window and compact it to the token budget (ADR-023, W1).

    Runs before the intent gate so every downstream decision sees the same bounded context. It is
    deliberately best-effort: a backend failure leaves the window empty and the run proceeds,
    because losing history degrades an answer but must never prevent one.
    """
    org_context = state.get("org_context") or {}
    org_id = org_context.get("organization_id") or org_context.get("org_id")
    conversation_id = org_context.get("conversation_id")

    if not org_id or not conversation_id:
        # A staff query with no bound conversation, or a caller that did not supply one.
        return {"history": [], "thread_summary": None, "pinned_slots": {}}

    settings = get_settings()
    try:
        payload = await ToolRegistry().get_conversation_history(
            str(org_id), str(conversation_id), limit=settings.context_window_turns
        )
    except Exception:  # noqa: BLE001 - context is an enhancement, never a precondition
        logger.exception(
            "Failed to load conversation history; continuing without a transcript window. "
            "conversationId=%s",
            conversation_id,
        )
        return {"history": [], "thread_summary": None, "pinned_slots": {}}

    turns = (payload or {}).get("items") or []
    if not isinstance(turns, list):
        turns = []

    window = await compact(
        turns,
        token_budget=settings.context_window_tokens,
        llm=workflow_llm_or_none(settings),
        prior_summary=state.get("thread_summary"),
        prior_pinned=state.get("pinned_slots"),
    )

    logger.debug(
        "Context window: %d turn(s) kept, %d compacted, ~%d tokens.",
        len(window.kept),
        len(turns) - len(window.kept),
        window.estimated_tokens,
    )

    return {
        "history": window.kept,
        "thread_summary": window.thread_summary,
        "pinned_slots": window.pinned_slots,
    }


async def run_supervisor(state: ConciergeState) -> dict[str, Any]:
    """Decide how the message is handled, as the routing authority (ADR-023, Decision 3).

    Runs after ``load_context`` so it can see the transcript, and before ``resolve_customer`` so it
    can decide whether resolving a customer is even relevant to the request.

    Deterministic rules run first as a cheap pre-filter; the LLM supervisor is consulted only for
    input the rules cannot classify. With no LLM configured this node is exactly the old intent
    gate, which is what keeps the offline path deterministic.

    The plan is stored as a plain dict so the state stays JSON-serializable for checkpointing.
    """
    settings = get_settings()
    rule_intent = classify_by_rules(state["message"])
    consultable = rule_intent.intent_type == "general_inquiry"

    plan = await supervise(
        state["message"],
        # Only hand the model a call it can act on: the rules already resolved everything else.
        # Temperature 0: routing is a decision, not a composition (ADR-023).
        llm=supervisor_llm_or_none(settings) if consultable else None,
        org_context=state.get("org_context"),
        history=state.get("history"),
        thread_summary=state.get("thread_summary"),
        pinned_slots=state.get("pinned_slots"),
    )
    logger.debug(
        "Supervisor decided %s -> agents=%s clarification=%s",
        plan.intent_type,
        plan.suggested_agents,
        bool(plan.clarification),
    )
    return {"intent": plan.model_dump()}


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

    # A genuine inbound customer message is one the API tagged with a direction (ADR-016);
    # everything else staff type in the Salon is a staff query answered directly (no customer draft).
    channel = org_context.get("channel")
    direction = org_context.get("direction")
    staff_query = not direction
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
        "channel": channel or "whatsapp",
        "direction": direction,
        "staff_query": staff_query,
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


def _first_attachment_reference(
    attachments: Any,
) -> tuple[str | None, str | None]:
    """Extract the first resolvable ``{kind, id}`` reference from the API's attachment list.

    ``org_context.attachments[].reference`` is the contract the bridge publishes; it is what the
    vision backend resolves tenant-scoped. A malformed or absent entry yields ``(None, None)`` so
    the legacy ``image_url`` arm stays the only path.
    """
    if not isinstance(attachments, list):
        return None, None
    for attachment in attachments:
        if not isinstance(attachment, dict):
            continue
        reference = attachment.get("reference")
        if not isinstance(reference, dict):
            continue
        kind = reference.get("kind")
        ref_id = reference.get("id")
        if kind and ref_id:
            return str(kind), str(ref_id)
    return None, None


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
    image_ref_kind, image_ref_id = _first_attachment_reference(org_context.get("attachments"))

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
        # The reference arm is the contract; `image_url` is the compatibility bridge.
        "image_ref_kind": image_ref_kind,
        "image_ref_id": image_ref_id,
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



async def run_commerce_agent(state: ConciergeState) -> dict[str, Any]:
    """Run the Commerce Agent (Slice 3 - Lina).

    Evaluates order deals, profit margins, loyalty discounts, triggers HITL
    approvals when thresholds are exceeded, and produces payment links and delivery plans.
    """
    org_context = state.get("org_context") or {}
    org_id = org_context.get("organization_id") or org_context.get("org_id")
    if not org_id:
        return {
            "commerce_output": {
                "agent": "commerce",
                "ran": True,
                "status": "skipped",
                "reason": "no organization context available",
            }
        }

    customer_id = org_context.get("customer_id")
    customer_name = org_context.get("customer_name")
    resolution = state.get("resolution") or {}
    if resolution.get("kind") == "resolved":
        customer_id = customer_id or resolution.get("customer_id")
        profile = resolution.get("profile") or {}
        customer_name = customer_name or profile.get("fullName")

    items = org_context.get("items") or []
    proposed_discount = float(org_context.get("proposed_discount") or 0.0)
    delivery_address = org_context.get("delivery_address")
    channel = org_context.get("channel") or "whatsapp"

    registry = ToolRegistry()
    graph = build_commerce_graph(registry, org_context=org_context)
    commerce_state = {
        "org_id": str(org_id),
        "order_id": org_context.get("order_id"),
        "customer_id": str(customer_id) if customer_id else None,
        "customer_name": customer_name,
        "items": items,
        "proposed_discount": proposed_discount,
        "delivery_address": delivery_address,
        "channel": channel,
        "message": state.get("message", ""),
    }
    result = await graph.ainvoke(commerce_state)
    output = result.get("output") or {
        "agent": "commerce",
        "ran": True,
        "status": result.get("status") or "success",
    }
    return {"commerce_output": output}


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
        clarification = _clarification_for(state)
        output: dict[str, Any] = {
            "intent": intent.get("intent_type", "general_inquiry"),
        }
        if clarification is not None:
            # The run asked instead of acting, so there is no specialist content to attach. This is
            # a first-class outcome now (ADR-023, Decision 4), not a side effect of a veto.
            output["clarification"] = clarification
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
    """Send a run to the right next node after customer resolution.

    Customer resolution no longer vetoes the run (ADR-023, Decision 4). A resolution miss is a
    *fact*: it means the customer is not on file, which is the normal state for every first-time
    contact. It only stops the run when asking is genuinely the right move - an ambiguous match
    the person can disambiguate, or an explicit staff ``@mention`` that matched nothing.
    """
    if _clarification_for(state) is not None:
        return "formulate_response"

    # A resolved-or-not miss is not a veto. Only skip the memory agent when the supervisor
    # explicitly said customer resolution is a precondition and it is genuinely out of reach
    # (no customer id and no phone to look up).
    intent = state.get("intent") or {}
    if intent.get("needs_customer_resolution"):
        resolution = state.get("resolution") or {}
        org_context = state.get("org_context") or {}
        has_customer = bool(
            (resolution.get("kind") == "resolved" and resolution.get("customer_id"))
            or org_context.get("customer_id")
            or org_context.get("phone_number")
        )
        if resolution.get("kind") == "not_found" and not has_customer:
            return "formulate_response"

    # The specialists always start at memory; its conditional edges walk the plan's ordered set.
    return "memory_agent"


def _clarification_for(state: ConciergeState) -> dict[str, Any] | None:
    """The clarification a run should produce instead of specialist work, if any.

    Two sources, in precedence order:

    1. **The supervisor asked a question.** It saw the transcript and decided it cannot proceed,
       so its wording is used verbatim.
    2. **Customer resolution needs a human answer.** An ``ambiguous`` match is always worth asking
       about. A ``not_found`` is only worth asking about when the lookup came from an explicit
       staff mention: for an inbound sender the number is already known (it is the sender's own
       identity), so asking for it is incoherent, and the run must continue instead.
    """
    intent = state.get("intent") or {}
    asked = intent.get("clarification")
    if isinstance(asked, str) and asked.strip():
        return {"kind": "asked", "question": asked.strip()}

    resolution = state.get("resolution") or {}
    kind = resolution.get("kind")

    if kind == "ambiguous":
        return resolution

    if kind == "not_found" and resolution.get("explicit_mention"):
        return resolution

    return None


def _route_after_memory(state: ConciergeState) -> str:
    intent = state.get("intent") or {}
    agents = intent.get("suggested_agents", [])
    org_context = state.get("org_context") or {}
    if "visual" in agents or org_context.get("image_url"):
        return "visual_agent"
    if "commerce" in agents:
        return "commerce_agent"
    return "formulate_response"


def _route_after_visual(state: ConciergeState) -> str:
    intent = state.get("intent") or {}
    agents = intent.get("suggested_agents", [])
    if "commerce" in agents or route_after_visual(state) == "commerce":
        return "commerce_agent"
    return "formulate_response"



# ---------------------------------------------------------------------------
# Graph construction
# ---------------------------------------------------------------------------


def _node_usage(result: Any) -> dict[str, Any]:
    """Extract an LLM ``usage`` dict from a node's returned state update, if any."""
    if isinstance(result, dict):
        usage = result.get("usage")
        if isinstance(usage, dict):
            return usage
    return {}


def _complete_node(
    name: str,
    collector: TelemetryCollector | None,
    started: float,
    *,
    usage: dict[str, Any] | None = None,
    error: Exception | None = None,
) -> None:
    """Finish an instrumented node: close its step row and record its metrics."""
    metrics = get_agent_metrics()
    if error is not None:
        error_class = type(error).__name__
        if collector is not None:
            collector.complete_current_step(status="Failed", error_code=error_class)
        metrics.record_node_failure(node=name, error_class=error_class)
        return

    tokens = usage or {}
    if collector is not None:
        collector.complete_current_step(
            status="Succeeded",
            input_tokens=int(tokens.get("input_tokens") or 0),
            output_tokens=int(tokens.get("output_tokens") or 0),
            cached_tokens=int(tokens.get("cached_tokens") or 0),
            provider=tokens.get("provider"),
            model=tokens.get("model"),
        )
    metrics.record_node(node=name, duration_s=time.perf_counter() - started)


def _instrumented_node(
    name: str,
    node_fn: Callable[..., Any],
    collector: TelemetryCollector | None,
) -> Callable[..., Any]:
    """Wrap a concierge node so it produces a step row, a span and node metrics.

    This is the real caller for ``TelemetryCollector`` and ``chain_of_thought_span`` (G-13):
    both existed but had no application call site before Slice 4b.
    """
    if inspect.iscoroutinefunction(node_fn):

        @functools.wraps(node_fn)
        async def async_node(state: ConciergeState) -> Any:
            if collector is not None:
                collector.start_step(name, step_kind=NODE_STEP_KIND)
            started = time.perf_counter()
            try:
                with chain_of_thought_span(f"agent.node.{name}"):
                    result = await node_fn(state)
            except Exception as exc:  # noqa: BLE001 - record then re-raise unchanged
                _complete_node(name, collector, started, error=exc)
                raise
            _complete_node(name, collector, started, usage=_node_usage(result))
            return result

        return async_node

    @functools.wraps(node_fn)
    def sync_node(state: ConciergeState) -> Any:
        if collector is not None:
            collector.start_step(name, step_kind=NODE_STEP_KIND)
        started = time.perf_counter()
        try:
            with chain_of_thought_span(f"agent.node.{name}"):
                result = node_fn(state)
        except Exception as exc:  # noqa: BLE001 - record then re-raise unchanged
            _complete_node(name, collector, started, error=exc)
            raise
        _complete_node(name, collector, started, usage=_node_usage(result))
        return result

    return sync_node


def build_concierge_graph(collector: TelemetryCollector | None = None):
    """Build and compile the concierge workflow graph (no checkpointer).

    Args:
        collector: Optional run collector. When supplied, every node produces a step row and
            node-level metrics; when omitted the graph behaves exactly as before.
    """
    graph = StateGraph(ConciergeState)

    def node(name: str, node_fn: Callable[..., Any]) -> Callable[..., Any]:
        return _instrumented_node(name, node_fn, collector)

    graph.add_node("load_context", node("load_context", run_load_context))
    graph.add_node("supervisor", node("supervisor", run_supervisor))
    graph.add_node("resolve_customer", node("resolve_customer", run_resolve_customer))
    graph.add_node("memory_agent", node("memory_agent", run_memory_agent))
    graph.add_node("visual_agent", node("visual_agent", run_visual_agent))
    graph.add_node("commerce_agent", node("commerce_agent", run_commerce_agent))
    graph.add_node("formulate_response", node("formulate_response", formulate_response))

    graph.add_edge(START, "load_context")
    graph.add_edge("load_context", "supervisor")
    graph.add_conditional_edges("supervisor", _route_after_intent)
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
    collector: TelemetryCollector | None = None,
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
        collector: Optional run collector. When supplied every node writes a step row and
            node metrics, and tool calls attach ToolCall rows to the same run.

    Returns:
        The final ``AgentResponse`` envelope.
    """
    graph = build_concierge_graph(collector=collector)
    initial: dict[str, Any] = {
        "message": message,
        "org_context": org_context or {},
        "history": [],
        "thread_summary": None,
        "pinned_slots": {},
        "intent": None,
        "resolution": None,
        "memory_output": None,
        "visual_output": None,
        "commerce_output": None,
        "usage": None,
        "response": None,
    }

    with use_telemetry_collector(collector):
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
