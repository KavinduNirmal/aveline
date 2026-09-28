"""Extract the customer's name when they introduce themselves (ADR-023 follow-up).

A customer's first inbound message often *is* their introduction ("I'm Kasha vivian, this is for a
cocktail party"). The shared free-text extractor does not serve this: it trusts title-cased runs, so
``I'm Kasha vivian`` yields ``"I'm Kasha"`` - a phrase, not a name - and ``Im kavindu`` yields
``"Im"``. Both are worse than nothing, because the result is written to the customer record.

This module is deliberately narrow. It only recognises an *introduction*, because that is the one
context where prose reliably contains a name that is not already on file. Broad extractors trade
silent false positives for the occasional miss, and a wrong name on a customer record is a visible,
trust-damaging defect.

Deterministic rather than LLM-based on purpose: this runs on every inbound message, must work with
no provider configured, and is unit-testable.
"""

import re

#: Lead-ins that reliably announce a name. Kept explicit rather than fuzzy: a lead-in that also
#: matches ordinary prose turns the whole extraction into a guess.
_LEAD_INS = (
    r"(?:i\s+am|i'?m|im|it'?s|its|this\s+is|my\s+name\s+is|name'?s|myself|here\s+is|call\s+me)"
)

#: Words that end a name and begin the rest of the sentence. Without this, the relative clause in
#: "I'm Kasha vivian, this is for a cocktail party" would be swallowed into the name.
_BOUNDARY_WORDS = frozenset(
    {
        "this", "that", "these", "those", "and", "but", "for", "from", "with", "about", "here",
        "there", "is", "are", "was", "were", "am", "looking", "want", "need", "interested",
        "hoping", "wondering", "asking", "planning", "trying", "coming", "going", "shopping",
        "after", "in", "on", "at", "to", "of", "the", "a", "an", "my", "our",
    }
)

#: One name word: capitalized ("Kasha") or lowercase ("kavindu"), alphabetic with internal
#: apostrophes/hyphens ("O'Brien", "Anne-Marie"). Lowercase is accepted because people type their
#: own name casually, and the lead-in is what makes it safe to do so.
_WORD = r"[A-Za-z][A-Za-z'\-]*"

#: Up to three name words, stopping at a boundary word or the end of the clause.
_TAIL = r"(?:\s+(?!(?:" + "|".join(_BOUNDARY_WORDS) + r")\b)" + _WORD + r")*"

_PATTERN = re.compile(
    r"\b(?:" + _LEAD_INS + r")\s+(?P<name>" + _WORD + _TAIL + r")",
    re.IGNORECASE,
)

#: A name of one word is common ("I'm Kasha") but is also where false positives concentrate, so a
#: single lowercase word is rejected unless it is clearly not an everyday word.
_MAX_NAME_WORDS = 4

#: Everyday words that a lead-in commonly introduces but that are never names. Only consulted for
#: the *first* captured word, which is the one that decides whether this is a name or prose.
_NON_NAME_WORDS = frozenset(
    {
        "good", "great", "fine", "well", "ok", "okay", "sorry", "sure", "glad", "happy", "sad",
        "interested", "looking", "hoping", "wondering", "asking", "trying", "thinking", "back",
        "done", "ready", "new", "old", "just", "still", "also", "not", "no", "yes", "late",
        "afraid", "curious", "keen", "lovely", "nice", "beautiful", "pretty", "perfect",
        "wearing", "after", "from", "with", "at", "in", "on", "so", "very", "really",
    }
)


def extract_self_introduced_name(message: str | None) -> str | None:
    """Return the name from an explicit self-introduction, or ``None``.

    Returns ``None`` rather than guessing when no introduction is present, or when the captured
    text does not look like a name. A miss is harmless (the customer stays nameless until they
    introduce themselves); a wrong name is not.
    """
    if not message or not message.strip():
        return None

    match = _PATTERN.search(message)
    if match is None:
        return None

    raw = match.group("name").strip()
    if not raw:
        return None

    words = [w for w in re.split(r"\s+", raw) if w]
    if not words or len(words) > _MAX_NAME_WORDS:
        return None

    # Drop a trailing possessive/contraction fragment accidentally captured.
    words = [w for w in words if w.strip("'-")]
    if not words:
        return None

    # Nothing but boundary words means the lead-in matched ordinary prose ("this is for a party").
    if all(w.lower() in _BOUNDARY_WORDS for w in words):
        return None

    # The first word decides whether this is a name or prose. An everyday word here ("I'm good
    # thanks", "it's lovely weather") means the lead-in introduced a sentence, not a person.
    if words[0].lower() in _NON_NAME_WORDS:
        return None

    # Casing carries the signal. People capitalize their own name even mid-sentence ("I'm Kasha
    # vivian"); prose after a lead-in stays lowercase ("This is a lovely dress"). So a lowercase
    # first word is only credible when the whole lead-in was typed lowercase too - which is how
    # someone casually writing "im kavindu" presents it - and is otherwise the start of a sentence.
    if not words[0][0].isupper():
        lead_in = match.group(0)[: len(match.group(0)) - len(raw)]
        if any(ch.isupper() for ch in lead_in):
            return None

    name = " ".join(words)
    # Title-case a uniformly lowercase name ("kasha vivian"); leave mixed casing alone.
    return name.title() if name.islower() or name.isupper() else name
