"""Entity-mention extraction for explicit customer resolution (ADR-019).

Staff can prefix an entity with a token so the resolver looks it up exactly instead of guessing
from free text:

    @Samantha Arias  -> customer name "Samantha Arias"
    @jason smith dropped by  -> customer name "jason smith"  (prose after the name is dropped)
    #0771234567 / #+94771234567  -> phone

``@`` captures a greedy name: consecutive alphabetic word tokens (spaces, apostrophes/hyphens
within a token allowed) are taken until a hard delimiter (sentence punctuation or another mention
token) or a trailing prose word in a small post-name stop set. ``#`` is followed by an optional
phone digit run. A backslash escapes a literal token (``\\@``, ``\\#``).

The parser is pure and free of any LLM call so it stays fast and unit-testable.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

# Word token: letters plus an internal apostrophe/hyphen (O'Brien, Mary-Jane).
_WORD_RE = re.compile(r"[A-Za-z][A-Za-z'’-]*")

# Characters that end a @name region outright (sentence punctuation + other mention tokens).
# Apostrophes/hyphens are deliberately NOT boundaries so O'Brien / Mary-Jane stay one name.
_HARD_BOUNDARY = set("@#+/\\.,;:!?()[]{}")

# Lowercased words that indicate prose resumed after the customer's name (dropped from the tail).
_POST_NAME_STOPS = frozenset(
    {
        # movement / arrival / comms often after a name in staff notes
        "dropped", "came", "comes", "coming", "visited", "arrived", "called", "messaged",
        "contacted", "sent", "messaging",
        # wants / actions
        "wants", "needs", "bought", "purchased", "asked", "calling",
        # auxiliaries / copula
        "will", "would", "can", "could", "should", "shall", "is", "are", "was", "were",
        "has", "had", "have", "do", "does", "did",
        # function words that resume prose (incl. pronouns/prepositions)
        "and", "with", "about", "next", "this", "that", "his", "her", "their", "the", "a",
        "an", "on", "for", "at", "to", "in", "of", "by", "later", "then", "please",
        "us", "me", "him", "them", "you", "my", "your", "our",
        # temporal / domain cues
        "today", "tomorrow", "yesterday", "recently", "last", "week", "month", "soon",
        # interrogatives / intent cues
        "any", "event", "events", "occasion", "what", "when", "how", "who", "check",
    }
)


@dataclass(frozen=True)
class Mentions:
    """Entity references detected in a message (ADR-019)."""

    customer: str | None = None
    phone: str | None = None


def extract_mentions(message: str | None) -> Mentions:
    """Return the customer-name and/or phone referenced in ``message``, else ``None`` fields."""
    if not message:
        return Mentions()

    customer = _first_customer_name(message)
    phone = _first_phone(message)
    return Mentions(customer=customer, phone=phone)


def _first_customer_name(message: str) -> str | None:
    """Return the first ``@<name>`` customer mention, or None."""
    for i, char in enumerate(message):
        if char != "@" or _is_escaped(message, i):
            continue

        rest = message[i + 1:]
        region = _truncate_at_boundary(rest)
        tokens = _WORD_RE.findall(region)
        if not tokens:
            continue

        name = _trim_trailing_stops(tokens)
        if name:
            return name
    return None


def _first_phone(message: str) -> str | None:
    """Return the first ``#<phone>`` mention (optional leading ``+``), kept verbatim for E.164."""
    match = re.search(r"(?<!\\)#(\+?[0-9]{9,12})", message)
    return match.group(1) if match else None


def _truncate_at_boundary(text: str) -> str:
    """Cut ``text`` at the first hard boundary character that ends a name region."""
    for i, ch in enumerate(text):
        if ch in _HARD_BOUNDARY:
            return text[:i]
    return text


def _trim_trailing_stops(tokens: list[str]) -> str | None:
    """Join ``tokens`` after trimming trailing prose words (ADR-019), or None if none remain."""
    name_tokens = list(tokens)
    while name_tokens and name_tokens[-1].lower() in _POST_NAME_STOPS:
        name_tokens.pop()
    return " ".join(name_tokens) if name_tokens else None


def _is_escaped(message: str, index: int) -> bool:
    """True when the char at ``index`` is preceded by an (odd) run of backslashes."""
    backslashes = 0
    j = index - 1
    while j >= 0 and message[j] == "\\":
        backslashes += 1
        j -= 1
    return backslashes % 2 == 1
