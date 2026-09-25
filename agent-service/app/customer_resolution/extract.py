"""Deterministic phone/name extraction for customer resolution (Issue #161).

The concierge orchestrator needs to decide whether a message mentions a customer before it
can look one up. Extraction is deliberately rule-based and free of any LLM call so it stays
fast, cheap, and unit-testable; a more nuanced extractor can layer on top later.
"""

import re

# A Sri Lankan phone: either a leading-0 10-digit local number or a +94/94 11-digit form.
# Spaces/hyphens are tolerated by only matching contiguous digit runs; callers may pass the
# raw text to the backend which normalizes to E.164.
_PHONE_RE = re.compile(r"\+?94\d{9}|0\d{9}")

# Tokens that are alphabetically "proper" (capitalized) in free text but are never customer
# names: greetings, interrogatives, auxiliaries, common openers, days/months and intent cues.
_NON_NAME_WORDS = frozenset(
    {
        # greetings / filler
        "Hi", "Hello", "Hey", "Hii", "Yo", "Welcome", "Thanks", "Thank", "Please", "Pls",
        # interrogatives / openers
        "Any", "What", "Which", "Who", "Whom", "When", "Where", "Why", "How", "Could",
        "Would", "Will", "Can", "Should", "Shall", "Do", "Does", "Did", "Is", "Are", "Was",
        "Were", "Have", "Has", "Had",
        # pronouns / determiners
        "I", "You", "We", "They", "He", "She", "It", "Me", "Us", "My", "Your", "Our",
        "Their", "His", "Her", "Its", "A", "An", "The", "This", "That", "These", "Those",
        # conjunctions / prepositions
        "And", "Or", "But", "So", "For", "Of", "In", "On", "At", "To", "From", "With",
        "About", "By", "As", "Into", "After", "Before", "During", "No", "Yes", "Not",
        # intent cues / domain words that may start a sentence capitalized
        "Remember", "Prefers", "Prefer", "Likes", "Favourite", "Favorite", "Available",
        "Price", "Prices", "Cost", "Discount", "Budget", "Stock", "Size", "Status", "Happy",
        "Happiest", "Good", "Great", "New", "Also", "Just", "Only",
        # weekday / month names when capitalized
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "January", "February", "March", "April", "May", "June", "July", "August",
        "September", "October", "November", "December",
    }
)

_WORD_RE = re.compile(r"[A-Za-z][A-Za-z']*")


def extract_phone(message: str | None) -> str | None:
    """Return the first plausible Sri Lankan phone number found in ``message``, or None."""
    if not message:
        return None
    match = _PHONE_RE.search(message)
    return match.group(0) if match else None


def extract_customer_name(message: str | None) -> str | None:
    """Return the most likely customer name (a run of capitalized words) in ``message``.

    Non-name capitalized tokens (greetings, interrogatives, intent cues, ...) are excluded.
    Returns None when no clear name is present.
    """
    if not message:
        return None

    best_run: list[str] = []
    current: list[str] = []
    for word in _WORD_RE.findall(message):
        if _is_proper(word) and word not in _NON_NAME_WORDS:
            current.append(word)
        else:
            if len(current) > len(best_run):
                best_run = current
            current = []

    if len(current) > len(best_run):
        best_run = current

    return " ".join(best_run) if best_run else None


def _is_proper(word: str) -> bool:
    """True when ``word`` is title-cased (first letter upper, remainder lower/alpha)."""
    return len(word) > 1 and word[0].isupper() and word[1:].islower()
