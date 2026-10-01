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
from collections.abc import Awaitable, Callable, Sequence
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

from app.context import has_inbound_media

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

#: Words that name a *piece* rather than a property of one. Deliberately narrower than the
#: ``item_search`` keyword tuple, which also carries search-shaped words ("stock", "size", "photo")
#: and the occasion words that make a styling request. This set answers one question only: does the
#: message point at something Elle can look up?
#:
#: Kept beside the table rather than imported from the memory agent's own noun list
#: (``app/agents/customer_memory/parsing.py``), because the two answer different questions - that
#: one decides an intent, this one decides a lane - and the gate must stay importable without the
#: agent package. The overlap is pointed at from both ends by tests instead.
_PIECE_NOUNS: tuple[str, ...] = (
    "dress",
    "saree",
    "sari",
    "blouse",
    "gown",
    "frock",
    "skirt",
    "lehenga",
    "kurta",
    "outfit",
    "dupatta",
    "shawl",
    "jumpsuit",
    "trousers",
    "shirt",
    "jacket",
)

#: One pattern per noun, allowing a plural. Built once: this runs on every inbound message.
_PIECE_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(rf"\b{noun}s?\b", re.IGNORECASE) for noun in _PIECE_NOUNS
)


def names_a_piece(message: str) -> bool:
    """Whether ``message`` names a garment Elle could look up.

    Used to widen the pricing lane rather than to change the intent: "how much is the pink dress?"
    is a pricing question *about a piece*, and the price and stock of that piece live in the visual
    agent's lane. The rule table can only record one intent per message, so the keyword table alone
    would hand commerce a total it has no way to compute.
    """
    return any(pattern.search(message) for pattern in _PIECE_PATTERNS)


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


#: High-precision shapes for questions about a customer's past purchases, order history, or client profile.
#: Checked before the order placement vocabulary so queries like "any recent purchases for Kavindu?"
#: or "purchase history of Nadia" route to Ava (customer_preference / memory) rather than dispatching
#: Elle (visual) and Lina (commerce) for an order placement action.
_CUSTOMER_HISTORY_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\b(?:recent|past|previous|prior|last|all)\s+(?:purchases?|orders?|history|transactions?|items?)\b",
        r"\b(?:purchase|order|transaction)\s+history\b",
        r"\b(?:purchases?|orders?|bought|purchased|ordered)\s+(?:for|by|of|from)\b",
        r"\b(?:any|check|show|list|tell|what)\b[^.?!]{0,30}\b(?:recent\s+)?(?:purchases?|orders?)\b",
        r"\b(?:what|which|anything)\b[^.?!]{0,30}\b(?:did|has|have)\b[^.?!]{0,30}\b(?:buy|bought|purchase|purchased|order|ordered)\b",
        r"\b(?:did|has|have)\b[^.?!]{0,30}\b(?:buy|bought|purchase|purchased|order|ordered)\s+(?:anything|something|before|recently)\b",
    )
)


def is_customer_history_query(message: str) -> bool:
    """Whether ``message`` asks about a customer's past purchases, orders, or history."""
    return any(pattern.search(message) for pattern in _CUSTOMER_HISTORY_PATTERNS)


#: High-precision shapes for customer memory context, notes, preferences, or event statements.
#: These represent notes/updates meant for Ava (the Customer Memory Agent) rather than inventory searches.
#: Checked before the item/pricing/order vocabulary so messages like "Context: The customer has a wedding",
#: "Kavindu has a wedding in December and prefers cotton gowns", or "Add a note: she likes green silk sarees"
#: route exclusively to Ava (customer_preference or event_query) without dispatching Elle (visual).
_CUSTOMER_NOTE_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        # Explicit context/note prefixes or commands
        r"\bcontext:\s*",
        r"\b(?:add|record|save|store|log)\s+(?:a\s+)?note\b",
        r"\b(?:customer|client)\s+note\b",
        r"\b(?:note|remember)\s+for\s+(?:this\s+)?customer\b",
        r"\b(?:about|for)\s+this\s+customer\b",
        r"\bthe\s+customer\s+(?:has|is|prefers|likes|dislikes|wants|wears|stated|mentioned|attended|attending)\b",
        r"\bthis\s+customer\s+(?:has|is|prefers|likes|dislikes|wants|wears|stated|mentioned)\b",
        # Explicit preference/constraint phrasing
        r"\b(?:remember\s+(?:that|she|he|they)?|keep\s+in\s+mind\s+that)\b",
        r"\b(?:i|he|she|they)\s+(?:only\s+)?(?:prefer|prefers|preferred|like|likes|dislike|dislikes|hate|hates)\b",
        r"\b(?:i|he|she|they)\s+(?:only\s+wears?|never\s+wears?)\b",
        r"\b(?:keep|stick)\s+to\s+[^.?!]{1,30}\s+(?:only|exclusively)\b",
        r"\bleave\s+[^.?!]{1,30}\s+(?:out|aside|entirely)\b",
        r"\b(?:allergic|sensitive)\s+to\b",
        # Inquiries about the customer's profile / preferences
        r"\b(?:what|tell me)\b[^.?!]{0,30}\b(?:do we know|are the preferences|likes|dislikes|profile|notes?)\b",
        r"\b(?:who is|tell me about)\s+@?[a-z\s]+\b",
    )
)

#: Search/shopping actions that indicate a genuine item search query rather than a pure memory note
_EXPLICIT_SEARCH_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\b(?:do\s+you\s+have|are\s+there\s+any|is\s+there\s+any|have\s+you\s+got)\b",
        r"\b(?:show\s+me|find\s+(?:me\s+)?|search\s+for|look\s+for|can\s+(?:i|you)\s+see)\b",
        r"\b(?:in\s+stock|available\s+in\s+stock|in\s+your\s+collection|in\s+inventory)\b",
        r"\b(?:send\s+(?:me\s+)?(?:pictures?|photos?|images?|options?|pieces?))\b",
        r"\b(?:what\s+(?:sarees?|dresses?|gowns?|blouses?|outfits?)\s+do\s+you\s+have)\b",
    )
)

_EVENT_WORDS: tuple[str, ...] = (
    "wedding",
    "birthday",
    "anniversary",
    "gala",
    "reception",
    "cocktail",
    "event",
    "occasion",
)

_PREFERENCE_WORDS: tuple[str, ...] = (
    "prefer",
    "prefers",
    "preferred",
    "preference",
    "like",
    "likes",
    "dislike",
    "dislikes",
    "hate",
    "hates",
    "allergic",
    "sensitive",
    "wear",
    "wears",
    "cotton",
    "silk",
    "linen",
    "nylon",
    "spandex",
    "synthetic",
)


def is_customer_note_or_preference(message: str) -> bool:
    """Whether ``message`` is a customer note, preference statement, or memory context."""
    return any(pattern.search(message) for pattern in _CUSTOMER_NOTE_PATTERNS)


def has_explicit_search(message: str) -> bool:
    """Whether ``message`` explicitly requests searching, viewing, or checking stock of items."""
    return any(pattern.search(message) for pattern in _EXPLICIT_SEARCH_PATTERNS)


def is_event_declaration(message: str) -> bool:
    """Whether a memory message is purely an event declaration rather than a preference statement."""
    lowered = message.lower()
    has_event = any(w in lowered for w in _EVENT_WORDS)
    has_pref = any(w in lowered for w in _PREFERENCE_WORDS)
    return has_event and not has_pref


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
#
# This is the *default* set per intent, not the last word on routing: :func:`agents_for` applies the
# one refinement the vocabulary cannot express (a price question that names a piece needs the piece).
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


def agents_for(intent_type: IntentType, message: str) -> list[AgentName]:
    """The agents this intent dispatches for *this* message.

    One refinement on top of :data:`_AGENT_ROUTING`, and the reason the table alone was not enough:
    ``pricing_query`` means "what does this cost", and a cost is only computable from the piece.
    "Can you give me a discount?" needs commerce alone, but "how much is the pink dress?" needs Elle
    to find the pink dress first - and the table can only record one intent per message, with pricing
    deliberately outranking item search ("how much is this dress?" *is* a price question).

    The consequence of getting this wrong was visible in a real thread: a staff message asking what
    discount could be given on a named dress was routed to memory and commerce, Elle never ran, Lina
    found no line items and skipped, and the only reply was Ava's customer brief - an answer to a
    question nobody had asked. What the person noticed was Lina's silence, and this refinement is
    only half of that fix; the other half is commerce answering a question it has no basket for
    (ADR-028).
    """
    agents = list(_AGENT_ROUTING[intent_type])
    if intent_type == "pricing_query" and names_a_piece(message):
        agents.insert(1, "visual")
    return agents


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

    # A question about customer purchase history, past orders, or client profile outranks the order
    # placement vocabulary below so queries like "any recent purchases for Kavindu?" or "purchase
    # history of Nadia" route to Ava (customer_preference / memory) rather than dispatching Elle
    # (visual) and Lina (commerce) for an order placement action.
    if is_customer_history_query(lowered):
        return IntentGateOutput(
            intent_type="customer_preference",
            suggested_agents=_AGENT_ROUTING["customer_preference"],
        )

    # A customer memory context, note, preference statement, or event update outranks the item
    # search vocabulary below when there is no explicit search command. This ensures messages like
    # "Context: The customer has a wedding", "Kavindu has a wedding in December and prefers cotton gowns",
    # or "Add a note: she likes green silk sarees" route exclusively to Ava (customer_preference / event_query)
    # rather than dispatching Elle (visual) for an item search.
    if is_customer_note_or_preference(lowered) and not has_explicit_search(lowered):
        intent: IntentType = "event_query" if is_event_declaration(lowered) else "customer_preference"
        return IntentGateOutput(
            intent_type=intent,
            suggested_agents=_AGENT_ROUTING[intent],
        )

    for intent_type, keywords in _RULE_KEYWORDS:
        if any(keyword in lowered for keyword in keywords):
            return IntentGateOutput(
                intent_type=intent_type,
                suggested_agents=agents_for(intent_type, message),
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
    "\n\nWhen the message is a customer note, preference statement, event declaration, context "
    "update, or question about a customer's profile (e.g. 'The customer has a wedding', 'Customer "
    "prefers cotton gowns', 'Remember she likes pastel sarees', 'Context: ...'), route it to "
    "`customer_preference` or `event_query` with agents: [\"memory\"]. Do NOT route `visual` or "
    "`commerce` for customer notes or memory updates unless the person explicitly asks to search "
    "or view products (e.g. 'show me', 'find', 'do you have in stock') or make a purchase."
    "\n\nWhen SPEAKER says boutique staff, the newest message may be an *instruction* about a "
    "customer rather than a question: to record something about them (\"add a note for this "
    "customer: he prefers green tea\"), to correct a detail, or to update their name or number. "
    "Route those to `customer_preference`: the memory agent owns what the boutique knows about a "
    "customer and is the only lane that can write it. The same words from a customer are a "
    "statement about themselves; from staff they are an instruction, and only the memory lane can "
    "tell the two apart."
)

#: The intents the deterministic rules settle without consulting the model.
#:
#: ADR-023 Decision 3 makes the supervisor *the routing authority* and the rules "a cheap pre-filter
#: [for] unambiguous cases"; plan wave W2.6 says the same thing as "demote rules to pre-filter +
#: fallback", and the ADR's own note is that with this decision "the keyword table stops being the
#: authority".
#:
#: This was previously an *allow*list of the three intents the model was consulted for, which
#: inverted that: a keyword hit counted as proof of unambiguity, so the substring table decided the
#: route for every intent it matched and the model was reached only for the bucket the table had
#: already failed to match. Hits are exactly where a substring table goes wrong - `_RULE_KEYWORDS`
#: matches "prefers" anywhere in the message, so a staff instruction to *record* a preference
#: ("please add a note for this customer: he prefers green tea") was locked to
#: `customer_preference`, the model was never consulted, and the memory agent's first-person
#: extractor then found nothing to store. The result was a silent no-op that read as a real answer.
#:
#: So the fast path is now the narrow, explicitly enumerated one and everything else is the
#: supervisor's to decide. `out_of_scope` stays: it is a domain guard whose entire purpose is a
#: deterministic refusal. `aveline_help` and `tenant_account` remain consultable because those
#: answers are *composed* (from the handbook, and from live figures) rather than merely routed.
_UNAMBIGUOUS_INTENTS = frozenset({"out_of_scope"})

#: The intents the model used to be consulted for, before Decision 3 was implemented as written.
#: Kept so the rollback lever restores the previous routing *exactly* rather than approximately
#: (ADR-023 §8), which is what makes it safe to turn off in production.
_LEGACY_CONSULTABLE_INTENTS = frozenset({"general_inquiry", "aveline_help", "tenant_account"})


def rules_are_authoritative(
    rule_intent: IntentGateOutput,
    *,
    suggested_agents: Sequence[str] = (),
    authoritative: bool = True,
) -> bool:
    """Whether a rule decision settles the turn with no model call (ADR-023, Decision 3).

    Deliberately one implementation. This decision previously existed three times - as this module's
    consultable set, as an inline set in :mod:`app.workflows.concierge_workflow` and again as the
    condition inside :func:`supervise` - so widening any one of them would have left the other two
    deciding the old way.

    ``authoritative=False`` is the rollback lever: it reproduces the pre-Decision-3 allowlist
    without touching anything else about how the model is used.
    """
    if "visual" in suggested_agents:
        # Media presence is read from ``org_context``, which the model is not shown and therefore
        # cannot weigh; consulting it here could only talk the run *out* of analysing a picture the
        # caller demonstrably attached. True under the rollback too: this branch is not part of the
        # change Decision 3 makes.
        return True
    if rule_intent.intent_type in _UNAMBIGUOUS_INTENTS:
        return True
    if not authoritative:
        return rule_intent.intent_type not in _LEGACY_CONSULTABLE_INTENTS
    return False

#: Intents whose answer is produced in code and which route **no** specialist. An image attached to
#: one of these does not make it a request to look at a garment - a photo of a receipt is still a
#: refund question - so the media lane must not claim it. Public because the graph's routing guards
#: enforce the same boundary as :func:`_with_the_media_lane`, and one list is what keeps the two
#: from drifting.
NO_SPECIALIST_INTENTS = frozenset({"tenant_account", "out_of_scope"})

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
    authoritative: bool = True,
) -> SupervisorPlan:
    """Decide how ``message`` should be handled (ADR-023, Decision 3; ADR-025; ADR-026).

    The supervisor is the routing authority (Decision 3) and the deterministic rules are both a
    cheap pre-filter for genuinely unambiguous input and the complete fallback when no model is
    configured. That pre-filter is :data:`_UNAMBIGUOUS_INTENTS` plus the media lane, and it is small
    on purpose: a keyword table that matches on substrings cannot certify that a message is
    unambiguous, and treating a hit as such is what left a staff instruction to record a preference
    locked to ``customer_preference`` with the model never consulted. See
    :func:`rules_are_authoritative`.

    ``aveline_help`` and ``tenant_account`` are consultable because their answers are *composed*
    rather than routed - the former from ``handbook_hits``, the latter from the live ``tenant_usage``
    figures or, for a question about its clients, from the ``customer_book``.

    With no LLM this returns the rule-based decision, which is the guarantee that CI and offline
    development stay deterministic (``app/llm/runtime.py``). A platform question degrades to the
    conversational path there, because there is no model to compose a grounded answer; an account
    question does not, because its figures are data and the reply can be built without a model.

    A supervisor failure degrades to the rule decision rather than raising: routing must always
    produce *something*, and the rules are a complete fallback rather than a partial one.
    """
    rule_intent = classify_by_rules(message)
    rule_plan = _with_the_media_lane(_rules_to_plan(rule_intent), org_context)

    if llm is None:
        return _with_a_bounded_reply(
            _or_general_inquiry(rule_plan),
            tenant_usage=tenant_usage,
            customer_book=customer_book,
            org_context=org_context,
        )

    if rules_are_authoritative(
        rule_intent, suggested_agents=rule_plan.suggested_agents, authoritative=authoritative
    ):
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
            _with_the_media_lane(_or_general_inquiry(rule_plan), org_context),
            tenant_usage=tenant_usage,
            customer_book=customer_book,
            org_context=org_context,
        )

    refined = _parse_plan(getattr(response, "content", None), message)
    if refined is None:
        logger.warning("Supervisor returned no usable plan; keeping the rule-based decision.")
        return _with_a_bounded_reply(
            _with_the_media_lane(_or_general_inquiry(rule_plan), org_context),
            tenant_usage=tenant_usage,
            customer_book=customer_book,
            org_context=org_context,
        )

    # The account lane is pinned first, then media: a question about the boutique's own figures is
    # not a request to look at a garment, so an accompanying image must not pull it into Elle's lane.
    refined = _with_the_media_lane(_pin_authoritative_lane(refined, rule_plan), org_context)
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


def _with_the_media_lane(plan: SupervisorPlan, org_context: dict[str, Any] | None) -> SupervisorPlan:
    """Route a run that carries an image to the visual lane, whatever its text says.

    The classifier reads **text only**, and an image is not text. A photo sent bare - which the
    webhook deliberately records rather than dropping - arrives with an empty message, and one sent
    with "what do you think of this?" carries no word from any rule keyword. Both land on
    ``general_inquiry``, whose only agent is memory, so the picture was never analysed and the
    customer was answered by a specialist that did not know they had sent one.

    Media presence is therefore treated as evidence rather than a hint: it is read from
    ``org_context`` (which the rules cannot see and the model is not shown) and it pins the lane in
    code. This is the same shape as ``_pin_authoritative_lane``: a fact the decision depends on
    belongs to the code that can actually observe it.

    The plan's own routing is otherwise respected. ``visual`` is added to the ordered agent set
    rather than replacing it, so a photo sent with "I want to buy this" still reaches commerce, and
    a run with no media is returned untouched - which is what keeps a text-only message off the
    vision path. The two lanes answered in code rather than by a specialist are left alone: an
    account question is answered from fetched figures, and an out-of-scope request is refused, so an
    image attached to either does not turn it into a request to look at a garment.
    """
    if (
        not has_inbound_media(org_context)
        or "visual" in plan.suggested_agents
        or plan.intent_type in NO_SPECIALIST_INTENTS
    ):
        return plan

    agents = list(plan.suggested_agents)
    # Before commerce when both are present: Elle's analysis is what Lina's deal is priced from,
    # and the graph's own edges already encode that order.
    insert_at = agents.index("commerce") if "commerce" in agents else len(agents)
    agents.insert(insert_at, "visual")

    return plan.model_copy(
        update={
            # An image with no recognisable words is still a request to look at a garment, which is
            # item search - not the conversational fallback the empty text would otherwise imply.
            "intent_type": "item_search" if plan.intent_type == "general_inquiry" else plan.intent_type,
            "suggested_agents": agents,
            # Whoever ends up answering, it is a specialist rather than the conversational default.
            "reply": None,
        }
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

    # Who is speaking changes what the newest message *is*, and the keyword table cannot see it.
    # The API declares the sender explicitly (`staff_query`), and an absent flag is read as the
    # inbound customer direction - the same fail-closed reading `may_read_tenant_account` uses, so a
    # new channel that forgets to declare itself cannot have its text treated as staff instruction.
    speaker = (
        "boutique staff, using the Salon console"
        if may_read_tenant_account(org_context)
        else "a customer, messaging the boutique"
    )
    lines.append(f"SPEAKER: {speaker}")

    lines.append(f"NEWEST MESSAGE:\n{message}")
    lines.append(_SUPERVISOR_INSTRUCTION)

    return [
        SystemMessage(content=assemble_system_prompt("supervisor", org_context)),
        HumanMessage(content="\n\n".join(lines)),
    ]


def _parse_plan(content: Any, message: str = "") -> SupervisorPlan | None:
    """Parse the supervisor's JSON reply, tolerating a code fence around it.

    Validates leniently rather than strictly: an unexpected key or an unknown agent name is
    dropped instead of discarding an otherwise usable decision. The plan's *content* is what
    matters here, and the driver re-validates the agent set anyway.

    ``message`` is carried only so the fallback agent set can apply the same per-message refinement
    the rules do (:func:`agents_for`): a model that names an intent but no agents must land on the
    set that intent means *for this message*, not on the bare default.
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
        suggested_agents=agents or agents_for(intent, message),
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
