"""Tests for self-introduced customer names.

A first inbound message frequently *is* the introduction ("I'm Kasha vivian, this is for a cocktail
party"). The customer stayed "Unknown customer" because the name was never read out of the message,
so the record was created on the phone number alone.

The extractor is intentionally narrow and the false-positive cases matter as much as the positives:
a wrong name written to a customer record is a visible defect, while a miss merely leaves them
nameless until they say it again.
"""

import pytest

from app.agents.customer_memory.introduction import extract_self_introduced_name

# ---------------------------------------------------------------------------
# The case that motivated this
# ---------------------------------------------------------------------------


def test_extracts_the_name_from_the_reported_message():
    # The reported text. Note "vivian" is lowercase: title-case-only extraction produced
    # "I'm Kasha", a phrase rather than a name.
    assert (
        extract_self_introduced_name("I'm Kasha vivian, this is for a cocktail party")
        == "Kasha vivian"
    )


@pytest.mark.parametrize(
    ("message", "expected"),
    [
        ("My name is Nimal Perera", "Nimal Perera"),
        ("my name is nimal perera", "Nimal Perera"),
        ("This is Kasha Vivian", "Kasha Vivian"),
        ("Hi, I am Anne-Marie O'Brien", "Anne-Marie O'Brien"),
        ("I am Samantha Arias", "Samantha Arias"),
        ("It's Jason Smith", "Jason Smith"),
        ("Call me Nimal", "Nimal"),
        ("Myself Kavindu Perera", "Kavindu Perera"),
        # A lowercase lead-in with a lowercase name is how someone casually writes it.
        ("im kavindu", "Kavindu"),
        ("this is kasha vivian", "Kasha Vivian"),
    ],
)
def test_extracts_common_introduction_forms(message, expected):
    assert extract_self_introduced_name(message) == expected


# ---------------------------------------------------------------------------
# False positives — a wrong name is worse than no name
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "message",
    [
        # The original incident message introduces nobody.
        "Hello there. Are there any pinkish gowns in your collection?",
        "I need a dress for a wedding",
        "I am looking for a pink gown",
        "Im interested in the pink gown",
        # A lead-in that introduces a sentence, not a person. The capitalised lead-in followed by a
        # lowercase word is what marks the difference.
        "This is a lovely dress",
        "it's for a cocktail party",
        "this is for a party",
        "I'm good thanks",
        "Hi it's lovely weather today",
        "Im good",
        "I'm just browsing",
        # A lead-in with nothing after it.
        "my name is",
        "I am",
        "",
        "   ",
    ],
)
def test_returns_none_rather_than_guessing(message):
    assert extract_self_introduced_name(message) is None


def test_lowercase_name_after_a_capitalised_lead_in_is_rejected():
    """The discriminating rule, pinned explicitly.

    "I'm Kasha vivian" is a name because the person capitalised it. "Im kavindu" is accepted
    because the whole lead-in was lowercase, so the writer was not capitalising anything. But a
    capitalised lead-in followed by a lowercase word is a sentence ("This is a lovely dress").
    """
    assert extract_self_introduced_name("im kavindu") == "Kavindu"
    assert extract_self_introduced_name("This is a lovely dress") is None
