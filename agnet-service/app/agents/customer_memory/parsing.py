"""Deterministic, rule-based message parsing for the Customer Memory Agent.

Extracts a structured intent and lightweight preference/event signals from a raw customer
message without an LLM. This keeps the agent's core behavior testable with plain assertions.
More nuanced extraction can be layered on top via an LLM later without changing the graph
contract.
"""

import re
from typing import Any

_OCCASION_KEYWORDS = ("wedding", "birthday", "party", "anniversary", "office", "function")
_COLOR_KEYWORDS = (
    "red", "blue", "bluish", "green", "emerald", "gold", "golden", "white", "black",
    "pink", "maroon", "pastel", "purple", "silk", "lace",
)
_SIZE_KEYWORDS = ("XS", "S", "M", "L", "XL", "XXL")
_BUDGET_RE = re.compile(r"(?:under|around|about|max|budget)?\s*(?:LKR|Rs\.?|rs\.?)?\s?(\d{3,7})(?:\s*[kK]\b)?")
_URGENCY_KEYWORDS = ("urgent", "asap", "today", "tomorrow", "soon", "quickly", "need it")

_ITEM_NOUNS = (
    "saree", "sari", "blouse", "dress", "gown", "suit", "kurta", "lehenga",
    "outfit", "dupatta", "shawl", "fabric",
)

# event type -> keyword mapping
_EVENT_TYPE_KEYWORDS = {
    "wedding": ("wedding", "bride", "groom"),
    "birthday": ("birthday",),
    "party": ("party", "reception", "gathering"),
    "office": ("office", "work function", "corporate"),
    "anniversary": ("anniversary",),
}


def detect_intent_type(message: str) -> str:
    """Return the primary intent type for ``message``.

    Mirrors the shared gate's intent vocabulary. Order matters: pricing and item words are
    strong signals and checked first.
    """
    lowered = message.lower()
    if any(w in lowered for w in ("price", "cost", "discount", "budget", "how much")):
        return "pricing_query"
    if any(w in lowered for w in _ITEM_NOUNS):
        return "item_search"
    if any(w in lowered for w in ("prefer", "like", "love", "hate", "remember")):
        return "customer_preference"
    if any(w in lowered for w in ("wedding", "birthday", "event", "occasion", "anniversary")):
        return "event_query"
    return "general_inquiry"


def detect_event_type(message: str) -> str | None:
    """Return the first event type keyword matched in ``message``, or None."""
    lowered = message.lower()
    for event_type, keywords in _EVENT_TYPE_KEYWORDS.items():
        if any(kw in lowered for kw in keywords):
            return event_type
    return None


def extract_iso_date(message: str) -> str | None:
    """Return the first ISO-8601 date (yyyy-mm-dd) found in ``message``, or None."""
    match = re.search(r"\b(20\d{2})-(\d{2})-(\d{2})\b", message)
    return match.group(0) if match else None


def _has_size(message: str) -> str | None:
    lowered = message.upper()
    for size in _SIZE_KEYWORDS:
        if re.search(rf"\b{size}\b", lowered):
            return size
    return None


def parse_message(message: str, intent_hint: str | None = None) -> dict[str, Any]:
    """Parse ``message`` into a structured intent plus preference/event signals.

    Args:
        message: The raw inbound message text.
        intent_hint: Optional intent type already decided by the upstream gate. When provided it
            is preferred over re-detection for the ``intent_type`` field.

    Returns:
        A dict with:
          ``parsed_intent`` — intent_type/occasion/color/size/budget/urgency,
          ``preference_signals`` — list of dicts {statement, is_explicit},
          ``event_type`` — detected event type or None,
          ``iso_date`` — detected ISO date or None.
    """
    lowered = message.lower()

    occasion = next((o for o in _OCCASION_KEYWORDS if o in lowered), None)
    color = next((c for c in _COLOR_KEYWORDS if c in lowered), None)
    size = _has_size(message)
    budget: float | None = None
    budget_match = _BUDGET_RE.search(message)
    if budget_match:
        number = int(budget_match.group(1))
        # "5k" style shorthand handled loosely; assume thousands when small and "k" present.
        if budget_match.group(0).strip().endswith(("k", "K")):
            number *= 1000
        budget = float(number)
    urgency = next((u for u in _URGENCY_KEYWORDS if u in lowered), None)

    intent_type = intent_hint or detect_intent_type(message)

    parsed_intent: dict[str, Any] = {"intent_type": intent_type}
    if occasion:
        parsed_intent["occasion"] = occasion
    if color:
        parsed_intent["color"] = color
    if size:
        parsed_intent["size"] = size
    if budget is not None:
        parsed_intent["budget"] = budget
    if urgency:
        parsed_intent["urgency"] = urgency

    # Explicit preferences from "I (don't) like/prefer/love/hate <thing>" patterns.
    preference_signals: list[dict[str, Any]] = []
    pref_re = re.compile(
        r"i (?:really )?(?:don'?t |do not )?(?:like|prefer|love|hate|dislike) ([a-z0-9 ]{2,40})",
        re.IGNORECASE,
    )
    for match in pref_re.finditer(message):
        statement = match.group(0).strip()
        negative = any(token in statement.lower() for token in ("don't", "do not", "hate", "dislike"))
        preference_signals.append(
            {
                "statement": statement,
                "preference_key": "general",
                "preference_value": match.group(1).strip(),
                "is_explicit": True,
                "negative": negative,
            }
        )

    return {
        "parsed_intent": parsed_intent,
        "preference_signals": preference_signals,
        "event_type": detect_event_type(message),
        "iso_date": extract_iso_date(message),
    }
