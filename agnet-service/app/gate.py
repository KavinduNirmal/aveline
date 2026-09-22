"""The Intent Gate — entry point for all inbound agent requests.

Classifies what the user wants, checks relevance/safety, and routes to the
correct agent(s). It is **hybrid**: deterministic keyword rules run first (fast,
cheap, testable); an optional LLM classifier refines ambiguous input.

The gate is shared infrastructure. It only decides routing — it never executes
slice business logic.
"""

import json
import logging
from collections.abc import Awaitable, Callable
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

logger = logging.getLogger("aveline.agent.gate")

IntentType = Literal[
    "order_placement",
    "item_search",
    "pricing_query",
    "customer_preference",
    "event_query",
    "out_of_scope",
    "general_inquiry",
]

AgentName = Literal["memory", "visual", "commerce"]

# Optional async LLM classifier: returns a refined output or None to keep rules.
LLMClassifier = Callable[[str], Awaitable["IntentGateOutput | None"]]

# Keyword -> intent type. Order matters: first match wins.
_RULE_KEYWORDS: list[tuple[IntentType, tuple[str, ...]]] = [
    # Purchase/order actions involve memory, visual verification, and commerce validation.
    ("order_placement", ("purchase", "buy", "checkout", "order", "place order", "reserve")),
    # Pricing words are strong signals and often co-occur with item words
    # (e.g. "how much is this dress?"), so they are checked next.
    ("pricing_query", ("price", "cost", "discount", "budget", "margin", "how much", "charge")),
    ("item_search", ("dress", "saree", "blouse", "outfit", "party", "bluish", "size", "stock", "inventory", "item", "photo", "image", "picture", "matching")),
    ("customer_preference", ("remember", "prefer", "prefers", "likes", "favorite", "hates", "dislikes")),
    ("event_query", ("event", "wedding", "birthday", "anniversary", "occasion")),
]

# Phrases that clearly fall outside the boutique domain.
_OUT_OF_SCOPE_KEYWORDS = (
    "write code",
    "python script",
    "javascript",
    "tell me a joke",
    "recipe",
    "weather",
    "news",
    "math problem",
)

# Which agents handle each intent.
_AGENT_ROUTING: dict[IntentType, list[AgentName]] = {
    "order_placement": ["memory", "visual", "commerce"],
    "item_search": ["memory", "visual"],
    "pricing_query": ["memory", "commerce"],
    "customer_preference": ["memory"],
    "event_query": ["memory"],
    "general_inquiry": ["memory"],
    "out_of_scope": [],
}


class IntentGateOutput(BaseModel):
    """Result of classifying and routing an inbound message."""

    model_config = ConfigDict(extra="forbid")

    intent_type: IntentType
    suggested_agents: list[AgentName] = Field(default_factory=list)
    requires_approval: bool = False
    is_relevant: bool = True
    safety_flags: list[str] = Field(default_factory=list)


def classify_by_rules(message: str) -> IntentGateOutput:
    """Classify ``message`` using deterministic keyword rules.

    Args:
        message: The raw inbound message text.

    Returns:
        An ``IntentGateOutput`` with the rule-based classification.
    """
    lowered = message.lower()

    if any(keyword in lowered for keyword in _OUT_OF_SCOPE_KEYWORDS):
        return IntentGateOutput(
            intent_type="out_of_scope",
            suggested_agents=[],
            is_relevant=False,
            safety_flags=["out_of_scope"],
        )

    for intent_type, keywords in _RULE_KEYWORDS:
        if any(keyword in lowered for keyword in keywords):
            return IntentGateOutput(
                intent_type=intent_type,
                suggested_agents=_AGENT_ROUTING[intent_type],
            )

    return IntentGateOutput(
        intent_type="general_inquiry",
        suggested_agents=_AGENT_ROUTING["general_inquiry"],
    )


# ---------------------------------------------------------------------------
# Supervisor (ADR-023, Decision 3)
# ---------------------------------------------------------------------------


class SupervisorPlan(IntentGateOutput):
    """The supervisor's routing decision.

    The routing authority in the concierge graph. ``suggested_agents`` is ordered and is the set
    the driver executes; ``clarification`` is a question to ask *instead of* routing, and is the
    only way a run may decline to produce specialist work.
    """

    clarification: str | None = Field(
        default=None,
        description="A question to ask the person instead of proceeding, or null.",
    )
    needs_customer_resolution: bool = Field(
        default=False,
        description="Whether resolving a specific customer is a precondition for useful work.",
    )


#: The supervisor returns a plan as JSON. Instructed here rather than via provider-native
#: structured output so the seam stays provider-agnostic and testable with a plain transcript.
_SUPERVISOR_INSTRUCTION = (
    "\n\nDecide how to handle the newest message. Reply with ONLY a JSON object in exactly "
    "this shape, with no prose and no code fences:\n"
    "{\n"
    '  "intent_type": "order_placement" | "item_search" | "pricing_query" | '
    '"customer_preference" | "event_query" | "out_of_scope" | "general_inquiry",\n'
    '  "agents": ["memory" | "visual" | "commerce", ...],\n'
    '  "needs_customer_resolution": true | false,\n'
    '  "clarification": "<a short question to ask instead, or null>",\n'
    '  "requires_approval": true | false\n'
    "}"
)


async def supervise(
    message: str,
    *,
    llm: Any | None = None,
    org_context: dict[str, Any] | None = None,
    history: list[dict[str, Any]] | None = None,
    thread_summary: str | None = None,
    pinned_slots: dict[str, str] | None = None,
) -> SupervisorPlan:
    """Decide how ``message`` should be handled (ADR-023, Decision 3).

    Deterministic rules run first as a cheap pre-filter: they are free, they are fast, and they are
    right for unambiguous input. The LLM supervisor is consulted only when the rules land on
    ``general_inquiry``, which is exactly the case they cannot resolve - an unrecognised
    vocabulary, or a reference that needs the conversation to interpret.

    With no LLM this returns the rule-based decision unchanged, which is the guarantee that CI and
    offline development stay deterministic (``app/llm/runtime.py``).

    A supervisor failure degrades to the rule decision rather than raising: routing must always
    produce *something*, and the rules are a complete fallback rather than a partial one.
    """
    rule_plan = _rules_to_plan(classify_by_rules(message))

    if llm is None or rule_plan.intent_type != "general_inquiry":
        return rule_plan

    try:
        response = await llm.ainvoke(
            _build_supervisor_messages(
                message,
                org_context=org_context,
                history=history,
                thread_summary=thread_summary,
                pinned_slots=pinned_slots,
            )
        )
    except Exception:  # noqa: BLE001 - routing must not break on a provider failure
        logger.exception("Supervisor call failed; falling back to the rule-based plan.")
        return rule_plan

    refined = _parse_plan(getattr(response, "content", None))
    if refined is None:
        logger.warning("Supervisor returned no usable plan; keeping the rule-based decision.")
        return rule_plan

    return refined


def _rules_to_plan(rules: IntentGateOutput) -> SupervisorPlan:
    """Lift a rule-based decision into a plan.

    ``needs_customer_resolution`` is false for the rule path: it is a judgement the rules cannot
    make, and defaulting to true would reinstate the veto this change removes.
    """
    return SupervisorPlan(
        intent_type=rules.intent_type,
        suggested_agents=list(rules.suggested_agents),
        requires_approval=rules.requires_approval,
        is_relevant=rules.is_relevant,
        safety_flags=list(rules.safety_flags),
    )


def _build_supervisor_messages(
    message: str,
    *,
    org_context: dict[str, Any] | None,
    history: list[dict[str, Any]] | None,
    thread_summary: str | None,
    pinned_slots: dict[str, str] | None,
) -> list[Any]:
    """Assemble the supervisor's prompt from the bounded context window (ADR-023).

    Imports are local so this module stays importable without the prompt package, keeping the
    deterministic gate cheap to unit-test.
    """
    from langchain_core.messages import HumanMessage, SystemMessage

    from app.prompts.assembly import assemble_system_prompt

    lines: list[str] = []

    if thread_summary:
        lines.append(f"SUMMARY OF EARLIER CONVERSATION:\n{thread_summary}")

    if pinned_slots:
        rendered = "\n".join(f"- {key}: {value}" for key, value in pinned_slots.items())
        lines.append(f"ESTABLISHED SO FAR:\n{rendered}")

    if history:
        recent = "\n".join(
            f"{turn.get('authorKind')}: {turn.get('text')}" for turn in history
        )
        lines.append(f"RECENT CONVERSATION (oldest first):\n{recent}")

    lines.append(f"NEWEST MESSAGE:\n{message}")
    lines.append(_SUPERVISOR_INSTRUCTION)

    return [
        SystemMessage(content=assemble_system_prompt("supervisor", org_context)),
        HumanMessage(content="\n\n".join(lines)),
    ]


def _parse_plan(content: Any) -> SupervisorPlan | None:
    """Parse the supervisor's JSON reply, tolerating a code fence around it.

    Validates leniently rather than strictly: an unexpected key or an unknown agent name is
    dropped instead of discarding an otherwise usable decision. The plan's *content* is what
    matters here, and the driver re-validates the agent set anyway.
    """
    if not isinstance(content, str) or not content.strip():
        return None

    cleaned = content.strip()
    if cleaned.startswith("```"):
        cleaned = cleaned.split("```")[1] if "```" in cleaned[3:] else cleaned[3:]
        if cleaned.startswith("json"):
            cleaned = cleaned[4:]
        cleaned = cleaned.strip()

    try:
        parsed = json.loads(cleaned)
    except json.JSONDecodeError:
        logger.warning("Supervisor reply was not valid JSON; keeping the rule-based decision.")
        return None

    if not isinstance(parsed, dict):
        return None

    intent = parsed.get("intent_type")
    if intent not in _AGENT_ROUTING:
        # An unknown intent would route to nothing; the rules already produced a valid one.
        logger.warning("Supervisor returned an unknown intent_type %r; ignoring it.", intent)
        return None

    agents = [
        agent
        for agent in (parsed.get("agents") or [])
        if agent in _VALID_AGENTS
    ]

    clarification = parsed.get("clarification")
    if not isinstance(clarification, str) or not clarification.strip():
        clarification = None
    else:
        clarification = clarification.strip()

    return SupervisorPlan(
        intent_type=intent,
        # An intent the rules know implies a set; trust the model's ordering when it named one,
        # otherwise fall back to the table so a plan is never empty by omission.
        suggested_agents=agents or list(_AGENT_ROUTING[intent]),
        requires_approval=bool(parsed.get("requires_approval", False)),
        is_relevant=True,
        clarification=clarification,
        needs_customer_resolution=bool(parsed.get("needs_customer_resolution", False)),
    )


#: The agent names a plan may name.
_VALID_AGENTS = frozenset({"memory", "visual", "commerce"})


async def infer_intent(
    message: str,
    customer_context: dict | None = None,
    llm_classifier: LLMClassifier | None = None,
) -> IntentGateOutput:
    """Infer the intent of ``message``, routing to the appropriate agents.

    Runs deterministic rules first. When the rules yield an ambiguous result
    (``general_inquiry``) and an ``llm_classifier`` is provided, the LLM is
    consulted to refine the classification. If the LLM returns ``None`` or an
    invalid result, the rule-based output is kept.

    Args:
        message: The raw inbound message text.
        customer_context: Optional customer context (reserved for future use).
        llm_classifier: Optional async callable that returns a refined
            ``IntentGateOutput`` or ``None``.

    Returns:
        The final ``IntentGateOutput``.
    """
    del customer_context  # reserved for future context injection
    rule_result = classify_by_rules(message)

    if llm_classifier is not None and rule_result.intent_type == "general_inquiry":
        try:
            refined = await llm_classifier(message)
        except Exception:  # noqa: BLE001 - a classifier failure must not break the gate
            logger.exception("LLM intent classifier failed; falling back to rules.")
            refined = None
        if refined is not None:
            logger.debug("Intent refined by LLM: %s", refined.intent_type)
            return refined

    return rule_result
