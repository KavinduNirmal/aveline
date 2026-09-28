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

#: The clause that names a preference, taken from "I (don't) <verb> <tail>". The negation and the
#: verb are *captured* rather than re-scanned afterwards: the regex already accepts the
#: apostrophe-less "dont", and matching the statement text against the literal "don't" afterwards
#: silently reported a dislike as a preference for every customer who types without an apostrophe
#: ("I dont like them" -> "The customer prefers them").
_PREFERENCE_RE = re.compile(
    r"\bi (?:really |truly |just |quite |so |absolutely )?"
    r"(?P<negation>don'?t |do not |never )?"
    r"(?P<verb>like|prefer|love|hate|dislike)\b"
    r"(?P<tail>[^.!?;]*)",
    re.IGNORECASE,
)

#: Verbs that are negative on their own, with no negation word in front of them.
_NEGATIVE_VERBS = frozenset({"hate", "dislike"})

#: Leading articles and possessives that are never part of the preference itself.
_PREFERENCE_LEAD_NOISE = ("the ", "a ", "an ", "any ", "some ", "my ", "our ", "your ")

#: A preference clause ends where the *reason* or *occasion* starts: "cotton for a beach wedding"
#: states a preference for cotton. Cutting here is what keeps the value a thing rather than a
#: sentence.
_PREFERENCE_TRAILING_CLAUSE = re.compile(
    r"\b(?:for|in|at|on|with|when|because|since|while|that|which|but)\b",
    re.IGNORECASE,
)

#: Words that name no preference at all. "I don't like them" refers to a noun phrase in an earlier
#: clause; storing "dislikes them" records nothing a reader can act on, and resolving the
#: antecedent is beyond a regex. The signal is dropped instead of persisted as nonsense.
_PREFERENCE_VALUE_STOPWORDS = frozenset({
    "them", "they", "it", "that", "this", "these", "those", "him", "her", "hers", "one", "ones",
    "thing", "things", "stuff", "everything", "anything", "something", "nothing", "me", "us",
    "you", "please", "too", "also", "either", "much", "very", "all", "been", "just", "only",
})

#: A trailing generic noun adds nothing once the material is named: "cotton pieces" -> "cotton".
_PREFERENCE_TRAILING_NOISE = frozenset({
    "pieces", "piece", "ones", "items", "item", "things", "thing", "stuff", "fabrics", "fabric",
    "clothes", "clothing", "garments", "garment", "material", "materials",
})

#: A comma-separated segment is only another item in the list if it opens like one. "silk, linen
#: and wool" continues the list; "them, they make my skin itchy" starts a new clause, and taking it
#: would invent a preference.
_PREFERENCE_CLAUSE_STARTERS = frozenset({
    "they", "it", "he", "she", "we", "you", "i", "but", "however", "although", "because", "since",
    "so", "then", "and", "or", "please", "as", "if", "when",
})

#: A preference is a short noun phrase, not a sentence.
_PREFERENCE_VALUE_MAX_WORDS = 4

# event type -> keyword mapping
_EVENT_TYPE_KEYWORDS = {
    "wedding": ("wedding", "bride", "groom"),
    "birthday": ("birthday",),
    "party": ("party", "reception", "gathering"),
    "office": ("office", "work function", "corporate"),
    "anniversary": ("anniversary",),
}

# What a customer says when something went wrong. Two vocabularies, deliberately separate:
# a *complaint* is a fault the boutique must act on, a *sentiment* is how the customer feels.
# The memory categories exist for both and nothing ever wrote either (gap A7), so the store held
# no record of a bad experience at all - only the preference half of what customers say.
_COMPLAINT_PHRASES = (
    "not happy", "unhappy", "disappointed", "complaint", "complain", "wrong item",
    "damaged", "defective", "broken", "never arrived", "not delivered", "late delivery",
    "delayed", "rude", "overcharged", "refund", "return it", "still waiting", "no response",
    "poor quality", "bad quality", "faulty",
)
_SENTIMENT_PHRASES = (
    "love it", "loved it", "absolutely love", "so happy", "delighted", "amazing", "beautiful work",
    "thank you so much", "excited", "perfect fit", "much appreciated", "very pleased",
)


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


def detect_experience_signal(message: str) -> str | None:
    """Return ``"complaint"``, ``"sentiment"`` or None for how the customer is reporting things.

    A complaint wins over a sentiment when both vocabularies match, because a fault is the one the
    boutique has to act on: "I love the saree but it arrived damaged" is a complaint with a
    compliment attached, not a compliment. These are the two memory categories the schema declared
    and no code path wrote (gap A7).
    """
    lowered = message.lower()
    if any(phrase in lowered for phrase in _COMPLAINT_PHRASES):
        return "complaint"
    if any(phrase in lowered for phrase in _SENTIMENT_PHRASES):
        return "sentiment"
    return None


def _has_size(message: str) -> str | None:
    lowered = message.upper()
    for size in _SIZE_KEYWORDS:
        if re.search(rf"\b{size}\b", lowered):
            return size
    return None


def _looks_like_a_list_item(segment: str) -> bool:
    """Whether a comma-separated ``segment`` continues the list rather than starting a clause."""
    words = [w for w in re.split(r"\s+", segment.strip()) if w]
    if not words or not all(re.fullmatch(r"[A-Za-z][A-Za-z'-]*", w) for w in words):
        return False
    return words[0].lower() not in _PREFERENCE_CLAUSE_STARTERS


def _clean_preference_value(raw: str) -> str | None:
    """Reduce a raw clause to the thing preferred, or ``None`` when it names nothing.

    ``None`` is the honest answer for a bare pronoun or a clause that opens a new sentence: the
    alternative is a stored "prefers them", which reads as a fact and is not one.
    """
    value = raw.strip().strip(",;:").strip()
    if not value:
        return None

    lowered = value.lower()
    for noise in _PREFERENCE_LEAD_NOISE:
        if lowered.startswith(noise):
            value = value[len(noise):].strip()
            lowered = value.lower()
            break

    # "cotton for a beach wedding" -> "cotton".
    trailing = _PREFERENCE_TRAILING_CLAUSE.search(value)
    if trailing and trailing.start() > 0:
        value = value[: trailing.start()].strip()

    words = [w for w in re.split(r"\s+", value) if w]
    # A trailing generic noun is dropped only when a real one remains in front of it.
    if len(words) > 1 and words[-1].lower().strip(".,") in _PREFERENCE_TRAILING_NOISE:
        words = words[:-1]
    if not words:
        return None
    if len(words) > _PREFERENCE_VALUE_MAX_WORDS:
        words = words[:_PREFERENCE_VALUE_MAX_WORDS]

    cleaned = " ".join(words).strip()
    if len(cleaned) < 2:
        return None
    if cleaned.lower() in _PREFERENCE_VALUE_STOPWORDS:
        return None
    # Every word being a stopword means the phrase names nothing on its own.
    if all(w.lower().strip(".,") in _PREFERENCE_VALUE_STOPWORDS for w in words):
        return None
    return cleaned


def _preference_values(tail: str) -> list[str]:
    """Every value a single "I like ..." clause names, in order.

    A clause is cut at the first comma that starts a new clause, and each surviving comma segment
    may itself be a list joined by "and"/"or".
    """
    segments = [segment for segment in re.split(r",", tail) if segment.strip()]
    if not segments:
        return []

    accepted = [segments[0]]
    for segment in segments[1:]:
        if _looks_like_a_list_item(segment):
            accepted.append(segment)
        else:
            # A new clause: the rest of the message is no longer describing the preference.
            break

    values: list[str] = []
    for segment in accepted:
        for piece in re.split(r"\s+(?:and|or|&)\s+", segment, flags=re.IGNORECASE):
            cleaned = _clean_preference_value(piece)
            if cleaned and cleaned not in values:
                values.append(cleaned)
    return values


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
          ``iso_date`` — detected ISO date or None,
          ``experience`` — "complaint" | "sentiment" | None.
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

    # Explicit preferences from "I (don't) like/prefer/love/hate <thing>" patterns. Polarity is read
    # from the match's own negation group (and from hate/dislike), never by re-scanning the matched
    # text for the apostrophe spelling: the regex accepts "dont", and the scan did not, so every
    # apostrophe-less dislike was stored as its opposite.
    preference_signals: list[dict[str, Any]] = []
    for match in _PREFERENCE_RE.finditer(message):
        statement = " ".join(match.group(0).split())
        negative = bool(match.group("negation")) or match.group("verb").lower() in _NEGATIVE_VERBS
        for value in _preference_values(match.group("tail")):
            preference_signals.append(
                {
                    "statement": statement,
                    "preference_key": "general",
                    "preference_value": value,
                    "is_explicit": True,
                    "negative": negative,
                }
            )

    return {
        "parsed_intent": parsed_intent,
        "preference_signals": preference_signals,
        "event_type": detect_event_type(message),
        "iso_date": extract_iso_date(message),
        "experience": detect_experience_signal(message),
    }
