"""The Intent Gate — entry point for all inbound agent requests.

Classifies what the user wants, checks relevance/safety, and routes to the
correct agent(s). It is **hybrid**: deterministic keyword rules run first (fast,
cheap, testable); an optional LLM classifier refines ambiguous input.

The gate is shared infrastructure. It only decides routing — it never executes
slice business logic.
"""

import json
import logging
import re
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
    # Help with Aveline itself: "how do I invite a staff member?", "what is a Blossom?".
    # Deliberately not `product_help`: `product` already means the boutique's inventory in this
    # codebase (it is Elle's whole lane), so `product_help` would read as "help with our products".
    "aveline_help",
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

#: High-precision shapes for "help me use Aveline". Checked after the out-of-scope guard and before
#: the routing keyword table, because the table cannot tell "how much is the Orchid plan?" (a
#: question about Aveline) from "how much is this saree?" (a question about a product).
#:
#: Precision matters more than recall here: a false positive routes a message to the handbook, which
#: either answers it or says it cannot - it never invents. A false negative is today's behaviour. The
#: set is tuned against the golden query set (ADR-025, Phase 6), not guessed once and frozen.
_AVELINE_HELP_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\bhow (?:do|does|can|would|should) (?:i|we|you)\b",
        r"\bhow to\b",
        r"\bwhere (?:do|can) (?:i|we|you)\b",
        r"\bwhat (?:is|are) (?:a |an |the )?aveline\b",
        r"\bhow does aveline\b",
        r"\bwhat does (?:the |a )?(?:plan|subscription|blossom|salon|approval|entitlement|quota|seat|invoice|handbook)\b",
        r"\b(?:is|are) there a way to\b",
        r"\b(?:handbook|documentation|user guide|tutorial)\b",
        r"\bblossoms?\b",
        r"\b(?:seed|bloom|orchid|rose)\b[^.]{0,16}\bplan\b",
        r"\bplan\b[^.]{0,16}\b(?:seed|bloom|orchid|rose)\b",
        r"\b(?:my|our) (?:plan|subscription|allowance|seats?|quota|entitlements?)\b",
        r"\b(?:invitation|invite) code\b",
        r"\bapi key\b",
        r"\bwebhook\b",
        r"\bfloor tag\b",
        r"\bnot measured\b",
        r"\bentitlements?\b",
        r"\btop up\b",
        r"\bpermissions?\b",
    )
)


def is_aveline_help(message: str) -> bool:
    """Whether ``message`` asks how Aveline itself works (ADR-025)."""
    return any(pattern.search(message) for pattern in _AVELINE_HELP_PATTERNS)


# Which agents handle each intent.
_AGENT_ROUTING: dict[IntentType, list[AgentName]] = {
    "order_placement": ["memory", "visual", "commerce"],
    "item_search": ["memory", "visual"],
    "pricing_query": ["memory", "commerce"],
    "customer_preference": ["memory"],
    "event_query": ["memory"],
    "general_inquiry": ["memory"],
    # A platform question is not a customer question: no specialist runs, and Aveline answers from
    # the handbook herself.
    "aveline_help": [],
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

    # A question about Aveline outranks the product/pricing vocabulary below: the routing table
    # cannot tell "how much is the Orchid plan?" from "how much is this saree?".
    if is_aveline_help(lowered):
        return IntentGateOutput(
            intent_type="aveline_help",
            suggested_agents=_AGENT_ROUTING["aveline_help"],
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
    only way a run may decline to produce specialist work. ``reply`` is the supervisor's own
    message to the person, used only when no specialist ends up speaking.
    """

    clarification: str | None = Field(
        default=None,
        description="A question to ask the person instead of proceeding, or null.",
    )
    needs_customer_resolution: bool = Field(
        default=False,
        description="Whether resolving a specific customer is a precondition for useful work.",
    )
    reply: str | None = Field(
        default=None,
        description=(
            "A short, customer-facing message from the supervisor herself. It is used only when "
            "no specialist produces content (a greeting, small talk, a general question); null "
            "when the specialists answer."
        ),
    )


#: What Aveline says when a conversational message needs an answer and no model wrote one. Kept
#: deterministic so the offline/CI path stays reproducible: a greeting must never go unanswered
#: just because the model is unavailable.
_DEFAULT_REPLY = (
    "Hello, I'm Aveline, the boutique's concierge. Tell me what you have in mind - an occasion, a "
    "colour, a piece you saw - and I'll take it from there."
)

#: Agents whose content *is* the answer. A reply beside one of them would be a second answer, so a
#: plan that routes one never carries a reply.
_CONTENT_AGENTS = frozenset({"visual", "commerce"})


#: The supervisor returns a plan as JSON. Instructed here rather than via provider-native
#: structured output so the seam stays provider-agnostic and testable with a plain transcript.
_SUPERVISOR_INSTRUCTION = (
    "\n\nDecide how to handle the newest message. Reply with ONLY a JSON object in exactly "
    "this shape, with no prose and no code fences:\n"
    "{\n"
    '  "intent_type": "order_placement" | "item_search" | "pricing_query" | '
    '"customer_preference" | "event_query" | "out_of_scope" | "general_inquiry" | "aveline_help",\n'
    '  "agents": ["memory" | "visual" | "commerce", ...],\n'
    '  "needs_customer_resolution": true | false,\n'
    '  "clarification": "<a short question to ask instead, or null>",\n'
    '  "reply": "<a short message of your own, or null>",\n'
    '  "requires_approval": true | false\n'
    "}"
    "\n\nUse `aveline_help` when the person is asking how Aveline itself works - a feature, a "
    "plan, Blossoms, an invitation, a setting. Those questions route no specialist: answer them "
    "yourself from the HANDBOOK excerpts."
    "\n\n`reply` is your own short, warm message to the person, used only when no specialist "
    "answers - a greeting, small talk, a thank-you, or a general question. Keep it short and in "
    "Aveline's voice. Set it to null when you route `visual` or `commerce`: they produce the "
    "content, and a second answer reads as noise."
    "\n\nWhen HANDBOOK excerpts are provided, they are the only source you may answer a platform "
    "question from. Do not invent features, prices, limits or policies, and do not quote Blossom "
    "amounts or per-action costs. If the excerpts do not answer the question, say so warmly and "
    "point the person at support rather than guessing."
    "\n\nAnswer as Aveline, in your own words: meet the question before you answer it, lead with "
    "the answer, and keep the detail to what they actually need. Never recite an excerpt or a "
    "table back at them, and do not list the sources in your text - they are shown beside your "
    "reply already."
)

#: The intents the model is consulted for. `aveline_help` joins the list because a platform
#: question is composed, not routed: Aveline answers it herself from the handbook.
_CONSULTABLE_INTENTS = frozenset({"general_inquiry", "aveline_help"})

#: What Aveline says when a platform question reached the model but no answer came back. Better a
#: plain admission than silence on a question the person explicitly asked about the product.
_HANDBOOK_MISS_REPLY = (
    "That one isn't in my handbook, I'm afraid, and I'd rather not guess. The Aveline team can "
    "help you properly - you'll find them under Contact."
)


async def supervise(
    message: str,
    *,
    llm: Any | None = None,
    org_context: dict[str, Any] | None = None,
    history: list[dict[str, Any]] | None = None,
    thread_summary: str | None = None,
    pinned_slots: dict[str, str] | None = None,
    handbook_hits: list[dict[str, Any]] | None = None,
) -> SupervisorPlan:
    """Decide how ``message`` should be handled (ADR-023, Decision 3; ADR-025 for the handbook).

    Deterministic rules run first as a cheap pre-filter: they are free, they are fast, and they are
    right for unambiguous input. The LLM supervisor is consulted when the rules land on
    ``general_inquiry`` (the case they cannot resolve - an unrecognised vocabulary, or a reference
    that needs the conversation) and on ``aveline_help`` (a platform question, which is composed
    from ``handbook_hits`` rather than routed).

    With no LLM this returns the rule-based decision, which is the guarantee that CI and offline
    development stay deterministic (``app/llm/runtime.py``). A platform question degrades to the
    conversational path there, because there is no model to compose a grounded answer.

    A supervisor failure degrades to the rule decision rather than raising: routing must always
    produce *something*, and the rules are a complete fallback rather than a partial one.
    """
    rule_intent = classify_by_rules(message)
    rule_plan = _rules_to_plan(rule_intent)

    if llm is None:
        return _or_general_inquiry(rule_plan)

    if rule_intent.intent_type not in _CONSULTABLE_INTENTS:
        return rule_plan

    try:
        response = await llm.ainvoke(
            _build_supervisor_messages(
                message,
                org_context=org_context,
                history=history,
                thread_summary=thread_summary,
                pinned_slots=pinned_slots,
                handbook_hits=handbook_hits,
            )
        )
    except Exception:  # noqa: BLE001 - routing must not break on a provider failure
        logger.exception("Supervisor call failed; falling back to the rule-based plan.")
        return _or_general_inquiry(rule_plan)

    refined = _parse_plan(getattr(response, "content", None))
    if refined is None:
        logger.warning("Supervisor returned no usable plan; keeping the rule-based decision.")
        return _or_general_inquiry(rule_plan)

    return _with_a_bounded_reply(refined)


def _or_general_inquiry(plan: SupervisorPlan) -> SupervisorPlan:
    """Degrade an unanswerable platform question to the conversational path.

    Reached when the rules classify ``aveline_help`` but no model is available to compose a grounded
    answer (or the call failed). Falling back to ``general_inquiry`` keeps Aveline's deterministic
    reply rather than leaving the question unanswered.
    """
    if plan.intent_type != "aveline_help":
        return plan

    return plan.model_copy(
        update={
            "intent_type": "general_inquiry",
            "suggested_agents": list(_AGENT_ROUTING["general_inquiry"]),
            "reply": _DEFAULT_REPLY,
        }
    )


def _with_a_bounded_reply(plan: SupervisorPlan) -> SupervisorPlan:
    """Keep the supervisor's own reply within its lane.

    Three bounds, all enforced here rather than trusted to the model:

    - A plan that routes a content specialist never carries a reply. Those agents answer, and two
      answers to one message read as noise.
    - A conversational message for which no model answer arrived still gets the deterministic
      reply, so a greeting is never left unanswered.
    - A platform question that produced no answer gets a plain admission rather than silence.
    """
    reply = plan.reply

    if _CONTENT_AGENTS.intersection(plan.suggested_agents):
        reply = None
    elif not reply:
        if plan.intent_type == "aveline_help":
            reply = _HANDBOOK_MISS_REPLY
        elif plan.intent_type == "general_inquiry":
            reply = _DEFAULT_REPLY

    if reply == plan.reply:
        return plan
    return plan.model_copy(update={"reply": reply})


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
        # A conversational message the rules cannot route has nobody else to answer it.
        reply=_DEFAULT_REPLY if rules.intent_type == "general_inquiry" else None,
    )


def _build_supervisor_messages(
    message: str,
    *,
    org_context: dict[str, Any] | None,
    history: list[dict[str, Any]] | None,
    thread_summary: str | None,
    pinned_slots: dict[str, str] | None,
    handbook_hits: list[dict[str, Any]] | None = None,
) -> list[Any]:
    """Assemble the supervisor's prompt from the bounded context window (ADR-023) and handbook.

    Imports are local so this module stays importable without the prompt package, keeping the
    deterministic gate cheap to unit-test.
    """
    from langchain_core.messages import HumanMessage, SystemMessage

    from app.context import render_context_block, render_handbook_block
    from app.prompts.assembly import assemble_system_prompt

    lines: list[str] = []

    # The same rendering the specialists get, so the transcript cannot drift between prompts.
    context_block = render_context_block(
        history=history, thread_summary=thread_summary, pinned_slots=pinned_slots
    )
    if context_block:
        lines.append(context_block)

    # Retrieved handbook excerpts, when the message looked like a platform question (ADR-025).
    handbook_block = render_handbook_block(handbook_hits)
    if handbook_block:
        lines.append(handbook_block)

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

    reply = parsed.get("reply")
    if not isinstance(reply, str) or not reply.strip():
        reply = None
    else:
        reply = reply.strip()

    return SupervisorPlan(
        intent_type=intent,
        # An intent the rules know implies a set; trust the model's ordering when it named one,
        # otherwise fall back to the table so a plan is never empty by omission.
        suggested_agents=agents or list(_AGENT_ROUTING[intent]),
        requires_approval=bool(parsed.get("requires_approval", False)),
        is_relevant=True,
        clarification=clarification,
        needs_customer_resolution=bool(parsed.get("needs_customer_resolution", False)),
        reply=reply,
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
