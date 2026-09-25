"""Recognise an explicit staff instruction to update a customer (ADR-023 follow-up).

Staff type things like "Please update this customer with the name Kasha Vivian Perera and number
0771234567" into the Salon. This module turns that into a structured, *explicit* instruction - or
returns ``None``.

Two deliberate constraints:

1. **Only an explicit instruction is honoured.** The verb must be there ("update", "change", "set",
   "correct", ...). A message that merely mentions a name is not an instruction to write one, and
   inferring intent from prose is how a wrong name ends up on a record.
2. **Only the fields actually stated are returned.** An instruction naming a phone but no name must
   not clear the name, so each field is independently present or absent.

The extraction is deterministic rather than LLM-driven: this path writes customer PII, so it should
be predictable, testable, and impossible to trigger by a model's imagination.
"""

import re
from dataclasses import dataclass

from app.customer_resolution.extract import extract_phone

#: Verbs that make a message an instruction. Without one, nothing is applied.
_UPDATE_VERBS = r"update|change|set|correct|fix|amend|save|record|store|rename"

#: Lead-ins that announce the name value.
_NAME_MARKERS = r"full\s+name|name"

#: Words that end a captured name.
_NAME_STOP_WORDS = frozenset(
    {
        "and", "number", "phone", "mobile", "contact", "no", "to", "with", "for", "please",
        "email", "address", "the", "a", "an", "as", "is", "be", "their", "his", "her",
    }
)

_WORD = r"[A-Za-z][A-Za-z'\-]*"
_INSTRUCTION_RE = re.compile(
    r"\b(?:" + _UPDATE_VERBS + r")\b",
    re.IGNORECASE,
)
#: "name is Kasha Vivian", "name: Kasha", "name Kasha Vivian Perera and number 07..."
_NAME_RE = re.compile(
    r"\b(?:" + _NAME_MARKERS + r")\s*(?:is|to|as|:|=)?\s*(?P<name>" + _WORD + r"(?:\s+" + _WORD + r")*)",
    re.IGNORECASE,
)

#: Verb-led forms that state no "name" marker: "rename this customer to Kasha Vivian",
#: "set the client as Nimal". Tried only when the marker form found nothing, so the more explicit
#: "with the name X" phrasing is never misread by this looser pattern.
_VERB_NAME_RE = re.compile(
    r"\b(?:" + _UPDATE_VERBS + r")\s+"
    r"(?:this\s+|the\s+|their\s+|that\s+)?"
    r"(?:customer|client|profile|record)?\s*"
    r"(?:to|as|is|:|=)\s+"
    r"(?P<name>" + _WORD + r"(?:\s+" + _WORD + r")*)",
    re.IGNORECASE,
)

#: A name longer than this is prose, not a name.
_MAX_NAME_WORDS = 5


@dataclass(frozen=True)
class CustomerUpdateInstruction:
    """An explicit staff instruction to change a customer's details.

    Either field may be ``None``; only stated fields are present, so an update cannot blank out a
    value the instruction never mentioned.
    """

    full_name: str | None = None
    phone_number: str | None = None

    @property
    def is_empty(self) -> bool:
        return self.full_name is None and self.phone_number is None


def extract_customer_update(message: str | None) -> CustomerUpdateInstruction | None:
    """Return the explicit update instruction in ``message``, or ``None``.

    ``None`` means "this is not an instruction to change customer details", which is the answer for
    the overwhelming majority of messages. An instruction that states neither a usable name nor a
    phone also returns ``None``: there is nothing to apply.
    """
    if not message or not message.strip():
        return None

    if not _INSTRUCTION_RE.search(message):
        return None

    full_name = _extract_name(message)
    phone = extract_phone(message)

    if full_name is None and phone is None:
        return None

    return CustomerUpdateInstruction(full_name=full_name, phone_number=phone)


def _extract_name(message: str) -> str | None:
    """Return the name stated by the instruction, or ``None``."""
    match = _NAME_RE.search(message) or _VERB_NAME_RE.search(message)
    if match is None:
        return None

    words = [w for w in re.split(r"\s+", match.group("name").strip()) if w]

    trimmed: list[str] = []
    for word in words:
        if word.lower().strip(":,") in _NAME_STOP_WORDS:
            break
        trimmed.append(word)
        if len(trimmed) >= _MAX_NAME_WORDS:
            break

    if not trimmed:
        return None

    name = " ".join(w.strip(".,;:") for w in trimmed).strip()
    return name or None
