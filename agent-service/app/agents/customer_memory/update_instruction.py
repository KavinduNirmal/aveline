"""Recognise an explicit staff instruction to update a customer (ADR-023 follow-up).

Staff type things like "Please update this customer with the name Kasha Vivian Perera and number
0771234567" - or "Please add a note for this customer: He prefers green tea over coffee" - into the
Salon. This module turns either into a structured, *explicit* instruction, or returns ``None``.

Three deliberate constraints:

1. **Only an explicit instruction is honoured.** The verb must be there ("update", "change", "set",
   "correct", ...). A message that merely mentions a name is not an instruction to write one, and
   inferring intent from prose is how a wrong name ends up on a record. A note phrase ("add a
   note", "remember that") is the same kind of explicit instruction for the note field.
2. **Only the fields actually stated are returned.** An instruction naming a phone but no name must
   not clear the name, so each field is independently present or absent.
3. **A name or phone is only read from an instruction that also carries an update verb.** A note's
   own text is prose the staff member wrote, and it can contain anything - "add a note: she asked
   about the name of a fabric" mentions a name, "call her on 0771234567" contains a phone. Without
   this constraint those words would be read as an instruction to rewrite the identity.

The extraction is deterministic rather than LLM-driven: this path writes customer PII, so it should
be predictable, testable, and impossible to trigger by a model's imagination.
"""

import re
from dataclasses import dataclass

from app.customer_resolution.extract import extract_phone

#: Verbs that make a message an instruction to change an identity field. Without one, no name or
#: phone is read from prose.
_UPDATE_VERBS = r"update|change|set|correct|fix|amend|save|record|store|rename"

#: Phrasings that make a message an instruction to *remember* something. Kept separate from
#: ``_UPDATE_VERBS`` because the two write different things: an update rewrites a field, a note
#: appends an observation. Bare "note" is deliberately absent - it is an ordinary verb ("I note the
#: price went up"), and matching it would turn prose into a write.
_NOTE_PHRASES = (
    r"add(?:ing)?\s+(?:a\s+)?note"
    r"|make\s+(?:a\s+)?note"
    r"|keep\s+(?:a\s+)?note"
    r"|note\s+down"
    r"|note\s+that"
    r"|remember\s+that"
    r"|please\s+note"
)

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

#: Matches an identity-update verb on its own. Used to decide whether a name/phone may be read at
#: all (constraint 3), separately from the question of whether the message is an instruction.
_UPDATE_VERB_RE = re.compile(r"\b(?:" + _UPDATE_VERBS + r")\b", re.IGNORECASE)

#: Any instruction - identity or note. Without a match the message is ordinary prose.
_INSTRUCTION_RE = re.compile(
    r"\b(?:" + _UPDATE_VERBS + r")\b|\b(?:" + _NOTE_PHRASES + r")\b",
    re.IGNORECASE,
)

#: The note phrase itself, located so the text after it can be captured as the note.
_NOTE_RE = re.compile(r"\b(?:" + _NOTE_PHRASES + r")\b", re.IGNORECASE)

#: The customer a note is about, when it is named between the note phrase and the value:
#: "add a note for this customer: ...", "add a note about the client: ...". That clause names the
#: *target*, so it is stripped rather than stored as part of the note. A colon does the same job and
#: is handled separately, because "about Kasha:" cannot be told from the note's first word by a
#: regex that does not know names.
_TARGET_CLAUSE_RE = re.compile(
    r"^\s*(?:for|about|on|regarding|re)\s+"
    r"(?:(?:this|the|that|our)\s+)?"
    r"(?:customer|client|profile|record|account)\b",
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
#:
#: The object noun is **required**, not optional. With it optional the verb could be read as an
#: ordinary noun and the following "is" as the value separator, so "the price update is live" was
#: extracted as an instruction to name the customer "live", and "the stock update is pending" as
#: "pending". Those messages contain the word "update" but are not instructions to change anything;
#: a command that rewrites a name names the customer it is about. Requiring the noun keeps every
#: command form ("rename this customer to X", "set the client as X") and rejects the noun reading.
_VERB_NAME_RE = re.compile(
    r"\b(?:" + _UPDATE_VERBS + r")\s+"
    r"(?:this\s+|the\s+|their\s+|that\s+|our\s+)?"
    r"(?:customer|client|profile|record)\b\s*"
    r"(?:to|as|is|:|=)\s+"
    r"(?P<name>" + _WORD + r"(?:\s+" + _WORD + r")*)",
    re.IGNORECASE,
)

#: A name longer than this is prose, not a name.
_MAX_NAME_WORDS = 5


@dataclass(frozen=True)
class CustomerUpdateInstruction:
    """An explicit staff instruction to change a customer's details.

    Any field may be ``None``; only stated fields are present, so an update cannot blank out a
    value the instruction never mentioned.
    """

    full_name: str | None = None
    phone_number: str | None = None
    #: What staff asked us to remember, in their own words. Stored against the bound customer, so a
    #: pronoun in the sentence ("He prefers green tea") resolves to that customer in context.
    #: Rewriting it would put words in the staff member's mouth and lose the observation's detail.
    note: str | None = None

    @property
    def is_empty(self) -> bool:
        return self.full_name is None and self.phone_number is None and self.note is None


def extract_customer_update(message: str | None) -> CustomerUpdateInstruction | None:
    """Return the explicit update instruction in ``message``, or ``None``.

    ``None`` means "this is not an instruction to change customer details", which is the answer for
    the overwhelming majority of messages. An instruction that states no usable name, phone or note
    also returns ``None``: there is nothing to apply.

    A note-only instruction (``"Please add a note for this customer: ..."``) *is* returned. It used
    to be discarded at the "neither a name nor a phone" check below, which is why a staff member's
    request to record something produced the ordinary customer brief and stored nothing, silently.
    """
    if not message or not message.strip():
        return None

    if not _INSTRUCTION_RE.search(message):
        return None

    # Constraint 3 in the module docstring: a note's own text is prose, so a name or phone inside it
    # is not an instruction. An identity field is read only when the message carries an update verb,
    # which is exactly the condition under which the old extraction ran.
    is_identity_instruction = bool(_UPDATE_VERB_RE.search(message))
    full_name = _extract_name(message) if is_identity_instruction else None
    phone = extract_phone(message) if is_identity_instruction else None
    note = _extract_note(message)

    if full_name is None and phone is None and note is None:
        return None

    return CustomerUpdateInstruction(full_name=full_name, phone_number=phone, note=note)


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


def _extract_note(message: str) -> str | None:
    """Return the text an instruction asked us to remember, or ``None``.

    The value is whatever follows the colon, or - when the staff member did not use one - whatever
    follows the instruction phrase and any "for this customer" clause. It is then cleaned, not
    paraphrased: the note is evidence of what a staff member observed, and a rewrite is where "he
    prefers green tea over coffee" quietly becomes "customer likes tea".
    """
    match = _NOTE_RE.search(message)
    if match is None:
        return None

    remainder = _TARGET_CLAUSE_RE.sub("", message[match.end():], count=1)

    # A colon is the explicit separator staff reach for: everything after it is the note, so the
    # text on either side ("He prefers green tea") is kept verbatim.
    colon = remainder.find(":")
    if colon != -1:
        remainder = remainder[colon + 1:]

    return _clean_note(remainder)


def _clean_note(text: str) -> str | None:
    """Trim a captured note to the value itself, or ``None`` when nothing is left."""
    note = text.strip()
    # A separator can survive when the phrase ended right at it ("note down: ...").
    note = note.lstrip(":-,–—;").strip()

    # Staff sometimes wrap the customer's own words in quotes; the quotes are punctuation, not the
    # note.
    if len(note) >= 2 and note[0] == note[-1] and note[0] in "\"'“”‘’":
        note = note[1:-1].strip()

    # "note that ..." and "remember that ..." introduce the clause, and the same word can survive
    # after the separator ("add a note: that he likes green tea"). It carries no content.
    note = re.sub(r"^that\b[\s:,-]*", "", note, flags=re.IGNORECASE).strip()
    return note or None
