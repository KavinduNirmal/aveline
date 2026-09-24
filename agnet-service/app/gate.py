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
    # A question about this boutique's *own account*: "how many Blossoms do I have left?", "how
    # many seats have I got?". Separate from `aveline_help`, which is answered from documentation:
    # these are answered from live figures, so they have a different source, a different freshness
    # and a staff-only audience (ADR-026).
    "tenant_account",
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


#: High-precision shapes for "what is left on this boutique's own account" (ADR-026). Checked
#: *before* :func:`is_aveline_help`, which already claims any mention of "blossom": "what is a
#: Blossom?" is a documentation question, but "how many Blossoms do I have left?" is a question
#: about this tenant's own balance, and the two cannot share a lane.
#:
#: The distinction is the tail, not the noun. A counter word alone is not enough - "how much does a
#: Blossom cost?" and "how many seats does the Orchid plan include?" are both documentation
#: questions, and both are deliberately left to ``aveline_help``. What marks a tenant question is
#: asking about *the asker's own* position: what is left, what remains, what they have.
_TENANT_ALLOWANCE_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        # "how many/much <counter> ... left / remaining / do I have / can I add"
        r"\bhow (?:many|much)\b[^.?!]{0,40}"
        r"\b(?:blossoms?|credits?|seats?|staff|customers?|users?)\b[^.?!]{0,30}"
        r"\b(?:left|remaining|used|do (?:i|we) have|have (?:i|we) got"
        r"|can (?:i|we) (?:add|invite|create|have))\b",
        # "how much do I have left?" - the counter word is in the previous turn.
        r"\bhow (?:much|many)\b[^.?!]{0,25}\b(?:do (?:i|we) have|have (?:i|we) got)\b"
        r"[^.?!]{0,15}\bleft\b",
        # "<counter> left / remaining"
        r"\b(?:blossoms?|credits?|seats?|customers?)\s+(?:left|remaining)\b",
        # "my/our <counter> ... left / remaining / balance"
        r"\b(?:my|our)\s+(?:blossoms?|credits?|seats?|customers?|staff|users?)\b"
        r"[^.?!]{0,25}\b(?:left|remaining|balance)\b",
        r"\b(?:blossom|credit)\s+balance\b",
        r"\b(?:am i|are we)\s+(?:near|at|over|close to)\s+(?:my|our)\s+limit\b",
    )
)

#: Shapes for "who are our clients?" - the **book** rather than an allowance. Kept apart from the
#: allowance patterns above because the two need different data: the allowance answers from a count
#: against the plan, the book from the clients themselves. Both land in ``tenant_account``, so the
#: audience gate and the numeral guard cover them identically; only the fetch and the fallback reply
#: differ (``is_customer_book_question``).
#:
#: A bare "<counter> left / remaining" is deliberately absent: that tail means an allowance, so
#: "how many customers do I have left" stays with the count and "how many customers do we have"
#: comes here.
_CUSTOMER_BOOK_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\b(?:who|which)\b[^.?!]{0,25}\b(?:our|my|the)\b[^.?!]{0,12}"
        r"\b(?:customers?|clients?|regulars?)\b",
        r"\b(?:list|show|see|tell me)\b[^.?!]{0,20}\b(?:our|my|all|the)\b[^.?!]{0,12}"
        r"\b(?:customers?|clients?|regulars?)\b",
        r"\b(?:our|my|the)\s+(?:customer|client)\s+(?:book|base|list|roster)\b",
        r"\bhow many (?:customers?|clients?)\b(?!\s+[^.?!]{0,20}\b(?:left|remaining)\b)",
        r"\b(?:do|does)\s+(?:we|i)\s+have\s+(?:any\s+)?(?:customers?|clients?)\b",
        r"\b(?:new|recent|active|top|loyal|best)\s+(?:customers?|clients?)\b",
    )
)

_TENANT_ACCOUNT_PATTERNS: tuple[re.Pattern[str], ...] = (
    *_TENANT_ALLOWANCE_PATTERNS,
    *_CUSTOMER_BOOK_PATTERNS,
)


def is_tenant_account(message: str) -> bool:
    """Whether ``message`` asks about this boutique's own account figures (ADR-026)."""
    if _is_interface_location_question(message):
        return False
    return any(pattern.search(message) for pattern in _TENANT_ACCOUNT_PATTERNS)


def is_customer_book_question(message: str) -> bool:
    """Whether an account question is about the client **book** rather than an allowance.

    Read by the workflow to decide which of the two reads to make, and by the reply builder to
    decide which figures to answer from. It is deliberately a property of the *message* rather than
    of the intent: one lane carries both, because they share an audience and a guard, and only the
    data behind them differs.
    """
    if _is_interface_location_question(message):
        return False
    return any(pattern.search(message) for pattern in _CUSTOMER_BOOK_PATTERNS)


#: Shapes that point at the *interface* rather than asking for a value. "Where can I see my Blossom
#: balance?" is documentation - the handbook says which screen - while "what is my Blossom balance?"
#: is a question about the account. Both carry the same possessive phrase, so the tense of the verb
#: is the only thing separating them, and the handbook lane keeps the locating form.
_INTERFACE_LOCATION_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\b(?:where|how)\b[^.?!]{0,30}\b(?:see|find|check|view|look at)\b",
        r"\b(?:show|tell) me (?:where|how)\b",
        r"\bis there a way to\b",
    )
)


def _is_interface_location_question(message: str) -> bool:
    """Whether ``message`` asks where a figure lives rather than what it is."""
    return any(pattern.search(message) for pattern in _INTERFACE_LOCATION_PATTERNS)


def may_read_tenant_account(org_context: dict[str, Any] | None) -> bool:
    """Whether this request may be shown the boutique's own account figures (ADR-026).

    Requires the caller to have said so *explicitly*: the API sends ``staff_query: true`` on the
    staff paths and ``false`` on the inbound customer path, so the failure mode of a new channel
    that forgets the flag is a missing answer rather than a leaked Blossom balance. A missing seat
    count is recoverable; a customer reading the boutique's balance is not.

    This deliberately does **not** reuse the ``direction``-based derivations in the workflow's
    memory and visual nodes. Those are identity heuristics that read an absent direction as staff,
    which is right for deciding whether a customer brief is wanted and wrong for money: it would
    open this lane for any future caller that simply forgot to declare itself.
    """
    return (org_context or {}).get("staff_query") is True


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
    # Nor is a question about the boutique's own account a customer question. Aveline answers it
    # from the fetched figures, so no specialist runs (ADR-026).
    "tenant_account": [],
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

    # A question about this boutique's own account outranks both: it is the more specific reading,
    # and `is_aveline_help` would otherwise claim it on the word "blossom" alone (ADR-026).
    if is_tenant_account(lowered):
        return IntentGateOutput(
            intent_type="tenant_account",
            suggested_agents=_AGENT_ROUTING["tenant_account"],
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
    '"customer_preference" | "event_query" | "out_of_scope" | "general_inquiry" | "aveline_help" | '
    '"tenant_account",\n'
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
    "question from. Do not invent features, prices, limits or policies, and never state what a "
    "Blossom costs or what an action is charged - pricing is not yours to publish. If the excerpts "
    "do not answer the question, say so warmly and point the person at support rather than guessing."
    "\n\nUse `tenant_account` when the person is asking about this boutique's own account - what is "
    "left of its Blossoms, its staff seats, or its active-customer allowance. When the asker is "
    "entitled to those figures they arrive in a TENANT ACCOUNT section: quote only the numbers it "
    "contains, and never estimate, recompute or round one. When no such section is present you do "
    "not have the figures, so say you cannot see them rather than answering from memory."
    "\n\n`tenant_account` also covers questions about the boutique's own clients. Those arrive in a "
    "CUSTOMER BOOK section when the asker is entitled to them. Name only the clients it lists, "
    "exactly as they are written there: never invent a client, and never carry one over from "
    "earlier in the conversation."
    "\n\nAnswer as Aveline, in your own words: meet the question before you answer it, lead with "
    "the answer, and keep the detail to what they actually need. Never recite an excerpt or a "
    "table back at them, and do not list the sources in your text - they are shown beside your "
    "reply already."
)

#: The intents the model is consulted for. `aveline_help` joins the list because a platform
#: question is composed, not routed: Aveline answers it herself from the handbook. `tenant_account`
#: joins it for the same reason, from live figures rather than from documentation.
_CONSULTABLE_INTENTS = frozenset({"general_inquiry", "aveline_help", "tenant_account"})

#: What Aveline says when a platform question reached the model but no answer came back. Better a
#: plain admission than silence on a question the person explicitly asked about the product.
_HANDBOOK_MISS_REPLY = (
    "That one isn't in my handbook, I'm afraid, and I'd rather not guess. The Aveline team can "
    "help you properly - you'll find them under Contact."
)

#: What Aveline says about the boutique's account when the request may see it but the figures could
#: not be read. An account question has a definite answer, so this admits a failure rather than
#: pretending the question was the wrong one.
_TENANT_UNAVAILABLE_REPLY = (
    "I couldn't reach your account figures just now, I'm afraid - they're under Usage in your "
    "dashboard. Do ask me again in a moment."
)

#: What Aveline says when the person asking is not staff. Deliberately says nothing about the
#: boutique's position: the fact that a balance exists is itself the boutique's business, and this
#: reply is the one a customer message would receive.
_TENANT_NOT_STAFF_REPLY = (
    "That's the boutique's own account rather than anything on your side, so it isn't mine to "
    "share here. The Aveline team can help you with your own account."
)

#: Intents that are *about a particular client*. They are answered by the memory agent, and when it
#: has no client to work with it produces nothing, so Aveline needs a reply of her own that says
#: what she is missing rather than what she could not find.
_CUSTOMER_SCOPED_INTENTS = frozenset({"customer_preference", "event_query"})

#: What Aveline says when a customer-scoped question could not be tied to a client. It names the
#: missing piece instead of claiming the question was unanswerable, because it usually is answerable
#: - the person simply did not say who.
_NO_CUSTOMER_REPLY = (
    "I couldn't tell which client you mean, I'm afraid - give me a name or a number and I'll pull "
    "up everything we have on them."
)


def _join_names(names: list[str]) -> str:
    """Join client names the way a person reads them: "A", "A and B", "A, B and C"."""
    if len(names) == 1:
        return names[0]
    return f"{', '.join(names[:-1])} and {names[-1]}"


def _customer_book_reply(book: dict[str, Any] | None) -> str:
    """A deterministic answer about the client book (ADR-026).

    The offline and fallback path, and the guard's replacement when a model reply quotes a figure
    the book does not contain. Every figure here is one the rendered ``CUSTOMER BOOK`` block also
    carries, so the reply and the block cannot state different numbers.

    It deliberately does not say *how many* clients are active: the highlights read is capped, so a
    count would be a floor presented as a total.
    """
    from app.context import customer_book_summary

    summary = customer_book_summary(book)
    if summary is None:
        return _TENANT_UNAVAILABLE_REPLY

    total = summary["total"]
    names = summary["names"]
    window = f"since {summary['active_since']}" if summary["active_since"] else "recently"

    if total == "0":
        return "Your client book is empty at the moment - nobody has been added yet."
    if not names:
        return f"You have {total} clients on the books, though none has been active {window}."

    return (
        f"You have {total} clients on the books. Most recently active {window}: "
        f"{_join_names(names)}."
    )


def _fallback_reply(intent_type: str) -> str | None:
    """Aveline's own reply for a plan that no specialist will answer.

    The last line of defence against silence. A plan with no content specialist in it leaves nobody
    to produce an answer, so if the model wrote no reply and the memory agent then had nothing to
    work with, the turn used to end with *no message at all*: the person's question sat in the
    thread unanswered and the client's "Aveline is working" indicator had nothing to resolve into.
    Reproduced from the production thread - "Who are our customers?" was classified
    ``customer_preference``, routed to memory-only, and memory skipped for want of a customer.

    ``out_of_scope`` returns ``None``: that outcome carries its own reason on the response status,
    and the publisher emits it before it ever looks at the reply.
    """
    if intent_type == "out_of_scope":
        return None
    if intent_type == "aveline_help":
        return _HANDBOOK_MISS_REPLY
    if intent_type in _CUSTOMER_SCOPED_INTENTS:
        return _NO_CUSTOMER_REPLY
    return _DEFAULT_REPLY


def _tenant_reply(snapshot: dict[str, Any] | None) -> str:
    """A deterministic answer built from the fetched figures (ADR-026).

    The offline and fallback path, and also the guard's replacement when a model reply quotes a
    number the account does not contain. Every figure here is one the rendered ``TENANT ACCOUNT``
    block also carries, so the reply and the block cannot state different numbers.
    """
    from app.context import tenant_summary

    figures = tenant_summary(snapshot)
    if figures is None:
        return _TENANT_UNAVAILABLE_REPLY

    parts = [f"You have {figures['blossoms_remaining']} Blossoms left for this period"]

    staff = figures.get("staff_remaining") or ""
    if staff == "0":
        parts.append("every staff seat is taken")
    elif staff:
        parts.append(f"{staff} staff {'seat' if staff == '1' else 'seats'} still free")

    customers = figures.get("customers_remaining") or ""
    if customers:
        parts.append(f"room for {customers} more active customers")

    sentence = ", ".join(parts) + "."
    if figures.get("period_end"):
        sentence += f" Your Blossoms reset on {figures['period_end']}."
    return sentence


async def supervise(
    message: str,
    *,
    llm: Any | None = None,
    org_context: dict[str, Any] | None = None,
    history: list[dict[str, Any]] | None = None,
    thread_summary: str | None = None,
    pinned_slots: dict[str, str] | None = None,
    handbook_hits: list[dict[str, Any]] | None = None,
    tenant_usage: dict[str, Any] | None = None,
    customer_book: dict[str, Any] | None = None,
) -> SupervisorPlan:
    """Decide how ``message`` should be handled (ADR-023, Decision 3; ADR-025; ADR-026).

    Deterministic rules run first as a cheap pre-filter: they are free, they are fast, and they are
    right for unambiguous input. The LLM supervisor is consulted when the rules land on
    ``general_inquiry`` (the case they cannot resolve - an unrecognised vocabulary, or a reference
    that needs the conversation), on ``aveline_help`` (a platform question, composed from
    ``handbook_hits``) and on ``tenant_account`` (a question about this boutique's own account,
    composed from the live ``tenant_usage`` figures or, for a question about its clients, from the
    ``customer_book``).

    With no LLM this returns the rule-based decision, which is the guarantee that CI and offline
    development stay deterministic (``app/llm/runtime.py``). A platform question degrades to the
    conversational path there, because there is no model to compose a grounded answer; an account
    question does not, because its figures are data and the reply can be built without a model.

    A supervisor failure degrades to the rule decision rather than raising: routing must always
    produce *something*, and the rules are a complete fallback rather than a partial one.
    """
    rule_intent = classify_by_rules(message)
    rule_plan = _rules_to_plan(rule_intent)

    if llm is None:
        return _with_a_bounded_reply(
            _or_general_inquiry(rule_plan),
            tenant_usage=tenant_usage,
            customer_book=customer_book,
            org_context=org_context,
        )

    if rule_intent.intent_type not in _CONSULTABLE_INTENTS:
        # Bounded even though no model is involved: the rules resolve *routing*, not the reply, and
        # a rule-decided plan that routes no content specialist would otherwise leave the turn with
        # nobody to answer it (see `_fallback_reply`).
        return _with_a_bounded_reply(
            rule_plan,
            tenant_usage=tenant_usage,
            customer_book=customer_book,
            org_context=org_context,
        )

    try:
        response = await llm.ainvoke(
            _build_supervisor_messages(
                message,
                org_context=org_context,
                history=history,
                thread_summary=thread_summary,
                pinned_slots=pinned_slots,
                handbook_hits=handbook_hits,
                tenant_usage=tenant_usage,
                customer_book=customer_book,
            )
        )
    except Exception:  # noqa: BLE001 - routing must not break on a provider failure
        logger.exception("Supervisor call failed; falling back to the rule-based plan.")
        return _with_a_bounded_reply(
            _or_general_inquiry(rule_plan),
            tenant_usage=tenant_usage,
            customer_book=customer_book,
            org_context=org_context,
        )

    refined = _parse_plan(getattr(response, "content", None))
    if refined is None:
        logger.warning("Supervisor returned no usable plan; keeping the rule-based decision.")
        return _with_a_bounded_reply(
            _or_general_inquiry(rule_plan),
            tenant_usage=tenant_usage,
            customer_book=customer_book,
            org_context=org_context,
        )

    refined = _pin_authoritative_lane(refined, rule_plan)
    return _with_a_bounded_reply(
        refined,
        tenant_usage=tenant_usage,
        customer_book=customer_book,
        org_context=org_context,
    )


def _pin_authoritative_lane(plan: SupervisorPlan, rules: SupervisorPlan) -> SupervisorPlan:
    """Stop the model from routing away from a lane whose answer is enforced in code.

    An account question is answered from fetched figures, and :func:`_with_a_bounded_reply`
    guarantees those figures come from the snapshot only while the plan stays in the lane. The
    model still writes the wording; it does not get to move the question out from under the guard
    that stops it inventing a balance.
    """
    if rules.intent_type != "tenant_account" or plan.intent_type == "tenant_account":
        return plan

    logger.info(
        "Supervisor re-routed a tenant-account question to %s; keeping the account lane so its "
        "figures stay snapshot-bound.",
        plan.intent_type,
    )
    return plan.model_copy(update={"intent_type": "tenant_account", "suggested_agents": []})


def _or_general_inquiry(plan: SupervisorPlan) -> SupervisorPlan:
    """Degrade an unanswerable platform question to the conversational path.

    Reached when the rules classify ``aveline_help`` but no model is available to compose a grounded
    answer (or the call failed). Falling back to ``general_inquiry`` keeps Aveline's deterministic
    reply rather than leaving the question unanswered.

    An account question is deliberately **not** degraded: its answer is fetched data rather than
    composed prose, so :func:`_with_a_bounded_reply` can answer it with no model at all.
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


#: Numeral-shaped tokens, including thousands separators and a decimal part.
_NUMERAL = re.compile(r"\d[\d,]*\.?\d*")


def _numerals(text: str) -> set[str]:
    """The numeral-shaped tokens in ``text``, normalised so equivalent spellings compare equal.

    ``1,234.50`` and ``1234.5`` reduce to the same token, so the account guard does not reject a
    reply merely for formatting a figure differently from the block it was given.
    """
    found: set[str] = set()
    for raw in _NUMERAL.findall(text or ""):
        token = raw.replace(",", "").rstrip(".")
        whole, _, fraction = token.partition(".")
        fraction = fraction.rstrip("0")
        found.add(f"{whole}.{fraction}" if fraction else whole)
    return found


def _with_a_bounded_reply(
    plan: SupervisorPlan,
    *,
    tenant_usage: dict[str, Any] | None = None,
    customer_book: dict[str, Any] | None = None,
    org_context: dict[str, Any] | None = None,
) -> SupervisorPlan:
    """Keep the supervisor's own reply within its lane.

    Four bounds, all enforced here rather than trusted to the model:

    - A plan that routes a content specialist never carries a reply. Those agents answer, and two
      answers to one message read as noise.
    - A plan that routes **no** content specialist always carries one. Whether the model wrote it,
      the account lane built it, or the fallback supplied it, the turn ends with a message: a
      request must never be answered with silence (see :func:`_fallback_reply`).
    - A platform question that produced no answer gets a plain admission rather than silence.
    - An account question gets its figures from the fetched snapshot, never from the model. The
      model may word the answer only when it was actually shown the figures, and only while every
      numeral it used appears in what it was shown. A Blossom balance is a fact about money, so a
      plausible-looking invention is rejected in favour of the deterministic reply rather than
      passed through (ADR-026).
    """
    from app.context import render_customer_block, render_tenant_block

    reply = plan.reply

    if _CONTENT_AGENTS.intersection(plan.suggested_agents):
        reply = None
    elif plan.intent_type == "tenant_account":
        # Keyed on the rendered blocks rather than on the data merely existing: they *are* what the
        # model was shown, so they are also exactly the figures it may quote. When they are empty
        # the model saw nothing, so it does not get to word an account answer at all - not even one
        # carrying no numerals, because "you have none left" is a claim about the account
        # regardless of whether it contains a digit.
        blocks = "\n".join(
            block
            for block in (
                render_tenant_block(tenant_usage),
                render_customer_block(customer_book),
            )
            if block
        )
        if blocks:
            if reply and not _numerals(reply) <= _numerals(blocks):
                logger.warning(
                    "Discarding a tenant-account reply that quoted a figure absent from the "
                    "figures it was given; answering from those figures instead."
                )
                reply = None
            if not reply:
                reply = (
                    _customer_book_reply(customer_book)
                    if customer_book
                    else _tenant_reply(tenant_usage)
                )
        elif may_read_tenant_account(org_context):
            reply = _TENANT_UNAVAILABLE_REPLY
        else:
            reply = _TENANT_NOT_STAFF_REPLY
    elif not reply:
        reply = _fallback_reply(plan.intent_type)

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
    tenant_usage: dict[str, Any] | None = None,
    customer_book: dict[str, Any] | None = None,
) -> list[Any]:
    """Assemble the supervisor's prompt from the bounded context window (ADR-023), the handbook
    (ADR-025) and this boutique's own figures (ADR-026).

    Imports are local so this module stays importable without the prompt package, keeping the
    deterministic gate cheap to unit-test.
    """
    from langchain_core.messages import HumanMessage, SystemMessage

    from app.context import (
        render_context_block,
        render_customer_block,
        render_handbook_block,
        render_tenant_block,
    )
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

    # The boutique's own live figures, when the request was allowed to fetch them (ADR-026). Placed
    # after the handbook so the two never read as one source: documentation is quoted, accounts and
    # client lists are read. At most one of the two is ever present - the workflow fetches the book
    # for a book question and the account figures otherwise.
    for block in (render_tenant_block(tenant_usage), render_customer_block(customer_book)):
        if block:
            lines.append(block)

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
