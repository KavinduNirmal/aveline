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

from langgraph.errors import GraphInterrupt
from langgraph.graph import END, START, StateGraph
from langgraph.types import Command, interrupt

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
from app.schemas.approvals import normalize_decision
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
    # True when the run is backed by a checkpoint thread. A real pause needs one: `interrupt()`
    # writes the pause into the checkpoint, and without a thread there is nothing to resume.
    # Set by `run_concierge` from the presence of `thread_id`, so an un-checkpointed run keeps
    # the pre-ADR-024 behaviour exactly (ADR-024, Decision 3).
    checkpointing: bool
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


async def run_commerce_approval(state: ConciergeState) -> dict[str, Any]:
    """Pause the run for human approval, and settle it when the decision arrives (ADR-024).

    This is a node of its own on purpose. ``interrupt()`` re-executes the node it is called in when
    the graph resumes, so putting the pause inside ``run_commerce_agent`` would re-evaluate the deal
    - and its tool calls - on every decision. Here the pause is the whole node: the evaluation before
    it is checkpointed and never re-run.

    The decision arrives as the return value of ``interrupt()`` and is fed back into the commerce
    sub-graph as ``approval_decision``, which is what routes it to settlement or to rejection. The
    supervisor is deliberately not consulted: its plan was made for the *original* message, and a
    decision is not a new question (ADR-024, invariant A3).
    """
    commerce = state.get("commerce_output") or {}
    resume_value = interrupt(_approval_pause_payload(commerce))

    decision = _decision_from_resume(resume_value)
    if decision is None:
        # A resume carrying an unrecognised decision settles nothing. The API refuses to send one
        # (`ApprovalDecisions.ToAgentDecision`), so reaching here means a caller bypassed it; the
        # pause is left un-settled rather than guessed at (ADR-024, invariant A4).
        logger.error("Unrecognised approval resume value: %r", resume_value)
        return {
            "commerce_output": {
                "agent": "commerce",
                "ran": True,
                "status": "error",
                "reason": "unrecognised approval decision",
            }
        }

    org_context = state.get("org_context") or {}
    comments = resume_value.get("comment") if isinstance(resume_value, dict) else None
    revised_discount = (
        resume_value.get("revised_discount") if isinstance(resume_value, dict) else None
    )
    # The decision payload wins over the original call's context: by the time a human decides, the
    # API has written the order and knows the values the settlement must quote. The checkpoint's
    # `org_context` is the fallback, for a caller that resumes with only a decision.
    order_id = _from_resume(resume_value, "order_id") or org_context.get("order_id")
    customer_name = _from_resume(resume_value, "customer_name") or org_context.get("customer_name")

    commerce_state = {
        "org_id": str(org_context.get("organization_id") or org_context.get("org_id") or ""),
        "order_id": order_id,
        "customer_id": str(org_context.get("customer_id") or "") or None,
        "customer_name": customer_name,
        "items": org_context.get("items") or [],
        "proposed_discount": _discount_rate(
            revised_discount if revised_discount is not None else org_context.get("proposed_discount")
        ),
        "delivery_address": org_context.get("delivery_address"),
        "channel": org_context.get("channel") or "whatsapp",
        "message": state.get("message", ""),
        "approval_decision": decision,
        "approval_comment": comments,
    }

    graph = build_commerce_graph(ToolRegistry(), org_context=org_context)
    result = await graph.ainvoke(commerce_state)
    logger.info("Commerce resumed from approval: decision=%s", decision)

    output = result.get("output") or {
        "agent": "commerce",
        "ran": True,
        "status": result.get("status") or "error",
    }
    return {"commerce_output": output}


def _approval_pause_payload(commerce: dict[str, Any]) -> dict[str, Any]:
    """What the owner needs to decide, and nothing else.

    It is published to the caller (and therefore into the API's response body) as the interrupt
    value, so it must stay JSON-serializable and free of anything the API should not persist.
    """
    deal = commerce.get("deal") or {}
    return {
        "kind": "approval_required",
        "approval_type": commerce.get("approval_type"),
        "approval_reason": commerce.get("approval_reason"),
        "summary": commerce.get("summary"),
        "requires_approval": bool(commerce.get("needs_approval", True)),
        "total": deal.get("total"),
        "margin": deal.get("margin"),
        "triggered_rules": deal.get("triggered_rules") or [],
        "action_required": commerce.get("action_required"),
    }


def _decision_from_resume(resume_value: Any) -> str | None:
    """Pull a canonical decision out of whatever the resume carried."""
    if isinstance(resume_value, dict):
        return normalize_decision(resume_value.get("decision"))
    return normalize_decision(resume_value)


def _from_resume(resume_value: Any, key: str) -> Any:
    """A non-blank value from the resume payload, or ``None``."""
    if not isinstance(resume_value, dict):
        return None
    value = resume_value.get(key)
    if value is None or (isinstance(value, str) and not value.strip()):
        return None
    return value


def _as_float(value: Any) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def _discount_rate(value: Any) -> float:
    """Read a *rate* (0.0-1.0) out of a resume payload, defaulting to no discount.

    The API's approval verbs speak in absolute money (it rewrites ``Order.Discount``), while the
    commerce tools speak in rates. Sending an amount here would be read as a rate and, at best,
    clamped to 100% off - so an out-of-range value is discarded with a warning rather than trusted.
    The API converts before sending; this is the guard for a caller that does not.
    """
    rate = _as_float(value)
    if 0.0 <= rate <= 1.0:
        return rate

    logger.warning("Ignoring out-of-range discount rate %r on resume; treating it as no discount.", value)
    return 0.0


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
    commerce = state.get("commerce_output") or {}
    if intent.get("intent_type") == "out_of_scope":
        response = AgentResponse(
            status=AgentStatus.out_of_scope,
            output={"reason": "Request is outside the boutique domain."},
            metadata=metadata,
        )
    elif commerce.get("status") == "pending_approval":
        # The run stopped for sign-off. Reported as its own terminal status, not as `success` with a
        # nested flag, because the API decides whether to create an order from this field alone
        # (ADR-024, Decision 2) and the run telemetry maps it to `PausedForApproval`.
        response = AgentResponse(
            status=AgentStatus.pending_approval,
            output={
                "intent": intent.get("intent_type", "general_inquiry"),
                "memory": state.get("memory_output"),
                "visual": state.get("visual_output"),
                "commerce": commerce,
                "approval": _approval_pause_payload(commerce),
            },
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


def _route_after_commerce(state: ConciergeState) -> str:
    """Pause for approval when commerce says so and the run can actually be paused.

    Without a checkpointer there is nowhere to record the pause, so the run finishes and reports
    ``pending_approval`` in its response envelope instead. That is the pre-ADR-024 shape, and it keeps
    a run with no thread id completely unchanged (ADR-024, Decision 3).
    """
    if not state.get("checkpointing"):
        return "formulate_response"

    commerce = state.get("commerce_output") or {}
    if commerce.get("status") == "pending_approval":
        return "commerce_approval"
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
            except GraphInterrupt:
                # A pause is the node doing its job, not failing at it. Closing the step as
                # `Succeeded` keeps a paused run out of the failure counters, while the run's own
                # status (`PausedForApproval`) is what says the workflow stopped short.
                _complete_node(name, collector, started)
                raise
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
        except GraphInterrupt:
            _complete_node(name, collector, started)
            raise
        except Exception as exc:  # noqa: BLE001 - record then re-raise unchanged
            _complete_node(name, collector, started, error=exc)
            raise
        _complete_node(name, collector, started, usage=_node_usage(result))
        return result

    return sync_node


def build_concierge_graph(
    collector: TelemetryCollector | None = None,
    checkpointer: Any | None = None,
):
    """Build and compile the concierge workflow graph.

    Args:
        collector: Optional run collector. When supplied, every node produces a step row and
            node-level metrics; when omitted the graph behaves exactly as before.
        checkpointer: Optional LangGraph checkpointer. It must be compiled **into** the graph, not
            handed to each call: ``aget_state`` - how a paused run is discovered - reads the
            checkpointer off the compiled graph and does not accept one as an argument.
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
    graph.add_node("commerce_approval", node("commerce_approval", run_commerce_approval))
    graph.add_node("formulate_response", node("formulate_response", formulate_response))

    graph.add_edge(START, "load_context")
    graph.add_edge("load_context", "supervisor")
    graph.add_conditional_edges("supervisor", _route_after_intent)
    graph.add_conditional_edges("resolve_customer", _route_after_resolve)
    graph.add_conditional_edges("memory_agent", _route_after_memory)
    graph.add_conditional_edges("visual_agent", _route_after_visual)
    graph.add_conditional_edges(
        "commerce_agent",
        _route_after_commerce,
        {
            "commerce_approval": "commerce_approval",
            "formulate_response": "formulate_response",
        },
    )
    graph.add_edge("commerce_approval", "formulate_response")
    graph.add_edge("formulate_response", END)

    return graph.compile(checkpointer=checkpointer)


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
        The final ``AgentResponse`` envelope. When the run paused for human approval the envelope
        carries ``status = pending_approval`` and the approval payload under ``output.approval``
        (ADR-024, Decision 3); resuming it is :func:`resume_concierge`.
    """
    graph = build_concierge_graph(collector=collector)
    initial: dict[str, Any] = {
        "message": message,
        "org_context": org_context or {},
        "history": [],
        "thread_summary": None,
        "pinned_slots": {},
        "intent": None,
        "checkpointing": thread_id is not None,
        "resolution": None,
        "memory_output": None,
        "visual_output": None,
        "commerce_output": None,
        "usage": None,
        "response": None,
    }

    pause: dict[str, Any] | None = None
    with use_telemetry_collector(collector):
        if thread_id is not None:
            config = {"configurable": {"thread_id": thread_id}}
            state_delay_ms = get_settings().agent_state_delay_ms
            async with create_checkpointer() as checkpointer:
                # Compiled with the checkpointer, not handed one per call: `aget_state` reads it off
                # the compiled graph, and that is how a paused run is discovered.
                graph = build_concierge_graph(collector=collector, checkpointer=checkpointer)
                if on_state is not None:
                    result = await run_graph_with_states(
                        graph,
                        initial,
                        config,
                        on_state,
                        state_delay_ms=state_delay_ms,
                    )
                else:
                    result = await graph.ainvoke(initial, config=config)
                pause = await _pending_pause(graph, config)
        elif on_state is not None:
            result = await run_graph_with_states(
                graph,
                initial,
                None,
                on_state,
                state_delay_ms=get_settings().agent_state_delay_ms,
            )
        else:
            result = await graph.ainvoke(initial)

    if pause is not None:
        return _paused_response(result, pause)

    return AgentResponse.model_validate(result["response"])


async def resume_concierge(
    thread_id: str,
    resume_value: dict[str, Any],
    on_state: Callable[[AgentState], Awaitable[None]] | None = None,
    collector: TelemetryCollector | None = None,
) -> AgentResponse:
    """Settle a paused run through its checkpoint (ADR-024, Decision 3).

    This is a **resume**, not a re-run. LangGraph re-executes only the node that interrupted - the
    ``commerce_approval`` node - so the supervisor is never consulted and the deal evaluation already
    in the checkpoint is never repeated. Replaying the original request instead would re-run every
    specialist and every tool call before the pause, which is what the previous re-query resume did.

    Args:
        thread_id: The paused conversation's checkpoint thread.
        resume_value: ``{"decision": "approved"|"rejected"|"revised", "comment": ..., ...}``.
        on_state: Optional lifecycle-state callback, as in :func:`run_concierge`.
        collector: Optional run collector; the resumed leg is its own run.

    Returns:
        The settled ``AgentResponse``. If the graph pauses again the envelope is
        ``pending_approval`` once more.

    Raises:
        NoPausedRunError: The thread has no checkpoint awaiting a decision.
    """
    graph = build_concierge_graph(collector=collector)
    config = {"configurable": {"thread_id": thread_id}}
    pause: dict[str, Any] | None = None

    with use_telemetry_collector(collector):
        async with create_checkpointer() as checkpointer:
            graph = build_concierge_graph(collector=collector, checkpointer=checkpointer)
            snapshot = await graph.aget_state(config)
            if snapshot is None or not snapshot.next:
                # Nothing is waiting on this thread. Resuming anyway would start a fresh run with a
                # decision as its input, which is exactly the re-query behaviour being replaced.
                raise NoPausedRunError(thread_id)

            if on_state is not None:
                result = await run_graph_with_states(
                    graph,
                    Command(resume=resume_value),
                    config,
                    on_state,
                    state_delay_ms=get_settings().agent_state_delay_ms,
                )
            else:
                result = await graph.ainvoke(Command(resume=resume_value), config=config)
            pause = await _pending_pause(graph, config)

    if pause is not None:
        return _paused_response(result, pause)

    return AgentResponse.model_validate(result["response"])


class NoPausedRunError(LookupError):
    """The thread has no checkpoint waiting for an approval decision."""

    def __init__(self, thread_id: str) -> None:
        super().__init__(f"Thread '{thread_id}' has no paused run awaiting a decision.")
        self.thread_id = thread_id


async def _pending_pause(graph: Any, config: dict[str, Any]) -> dict[str, Any] | None:
    """The interrupt the graph is currently waiting on, or ``None`` when it ran to completion.

    Asked of the **checkpoint** rather than inferred from the stream: a paused run's event stream ends
    with a partial state and no error, so the checkpoint's ``next`` is the only honest signal that the
    workflow stopped short of an answer. The graph must have been compiled with its checkpointer,
    because this reads it from the graph itself.
    """
    snapshot = await graph.aget_state(config)
    if snapshot is None or not snapshot.next:
        return None

    for task in snapshot.tasks or ():
        for interrupt_ in getattr(task, "interrupts", ()) or ():
            value = getattr(interrupt_, "value", None)
            return value if isinstance(value, dict) else {"value": value}

    return {}


def _paused_response(result: dict[str, Any], pause: dict[str, Any]) -> AgentResponse:
    """Build the ``pending_approval`` envelope for a run that stopped for sign-off."""
    intent = result.get("intent") or {}
    return AgentResponse(
        status=AgentStatus.pending_approval,
        output={
            "intent": intent.get("intent_type", "general_inquiry"),
            "memory": result.get("memory_output"),
            "visual": result.get("visual_output"),
            "commerce": result.get("commerce_output"),
            "approval": pause,
        },
        metadata=_build_usage_metadata(result.get("usage")),
    )
