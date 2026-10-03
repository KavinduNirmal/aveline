"""Tests for recognising an explicit staff instruction to update a customer.

This path writes customer PII, so the negative cases carry as much weight as the positives: a
message that merely *mentions* a name must not be read as an instruction to store one.
"""

import pytest

from app.agents.customer_memory.update_instruction import extract_customer_update

# ---------------------------------------------------------------------------
# The reported instruction
# ---------------------------------------------------------------------------


def test_extracts_the_reported_instruction():
    result = extract_customer_update(
        "Please update this customer with the name Kasha Vivian Perera and number 0771234567"
    )

    assert result is not None
    assert result.full_name == "Kasha Vivian Perera"
    assert result.phone_number == "0771234567"


@pytest.mark.parametrize(
    ("message", "name", "phone"),
    [
        ("update the name to Kasha Vivian Perera", "Kasha Vivian Perera", None),
        ("change phone number to 0771234567", None, "0771234567"),
        ("set the name as Nimal and phone 0771234567", "Nimal", "0771234567"),
        ("Update name: Anne-Marie O'Brien", "Anne-Marie O'Brien", None),
        ("Please rename this customer to Kasha Vivian", "Kasha Vivian", None),
        ("correct the number to +94771234567", None, "+94771234567"),
        ("set the client as Nimal Perera", "Nimal Perera", None),
        ("update this customer to Kasha Vivian", "Kasha Vivian", None),
        ("change the customer's name to Anne Marie", "Anne Marie", None),
    ],
)
def test_extracts_the_stated_fields(message, name, phone):
    result = extract_customer_update(message)

    assert result is not None
    assert result.full_name == name
    assert result.phone_number == phone


def test_only_the_stated_field_is_returned():
    """An instruction naming a number must not be read as clearing the name.

    Each field is independently present or absent, so a partial update stays partial.
    """
    result = extract_customer_update("change the phone number to 0771234567")

    assert result is not None
    assert result.full_name is None
    assert result.phone_number == "0771234567"


# ---------------------------------------------------------------------------
# Not instructions
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "message",
    [
        # The reported incident messages, and ordinary prose.
        "Hello there. Are there any pinkish gowns in your collection?",
        "I am looking for a pink gown",
        "My name is Kasha vivian",
        "Please recheck if you have anything pink",
        "what is the name of this customer?",
        # An instruction about something that is not a customer's identity. "update" and "set" are
        # ordinary words; the extraction must not treat any sentence containing them as a write.
        "can you update the price to 5000",
        "update the stock level for this piece",
        "set the occasion to a cocktail party",
        # The same words used as *nouns*, with a copula after them. These read as an identity
        # instruction only if the verb-led pattern is allowed to fire with no object noun, which
        # turned "the price update is live" into a customer named "live" (gap A1).
        "the price update is live",
        "the update is ready",
        "update is complete",
        "the stock update is pending",
        # Adversarial wording that talks *about* updating without instructing one.
        "update me on the status of my order",
        "set up a fitting appointment for tomorrow",
        "please update the customer on the delivery date",
        "I will update you once the fabric arrives",
        "disregard the note. update the delivery window",
        # Degenerate input.
        "",
        "   ",
    ],
)
def test_is_not_an_update_instruction(message):
    assert extract_customer_update(message) is None


def test_a_verb_used_as_a_noun_with_a_copula_is_not_an_instruction():
    """The class behind "the price update is live": noun "update" + "is" + a state word.

    Each of these has an update verb *somewhere* and a name-shaped word after "is", so the old
    looser pattern extracted the state word as the customer's name. None of them instructs a change.
    """
    for message in (
        "the price update is live",
        "the update is ready",
        "update is complete",
        "the stock update is pending",
        "the price update is live, set the stock level to zero",
    ):
        assert extract_customer_update(message) is None, message


@pytest.mark.parametrize(
    ("message", "name"),
    [
        # The legitimate copula form, inside an actual instruction, must keep working.
        ("update this customer - name is Ana", "Ana"),
        ("update the profile, name is Ana", "Ana"),
        ("set the client as Nimal", "Nimal"),
        ("Please rename this customer to Kasha Vivian", "Kasha Vivian"),
    ],
)
def test_the_legitimate_instruction_forms_still_extract(message, name):
    result = extract_customer_update(message)

    assert result is not None, message
    assert result.full_name == name


def test_an_instruction_stating_nothing_usable_is_not_an_instruction():
    # "update" with neither a name nor a phone gives nothing to apply.
    assert extract_customer_update("please update this customer") is None


def test_none_message_is_safe():
    assert extract_customer_update(None) is None


# ---------------------------------------------------------------------------
# Notes
# ---------------------------------------------------------------------------
#
# "Please add a note for this customer: He prefers green tea over coffee" is a staff instruction to
# remember something, not to change an identity field. It used to match no instruction verb and was
# discarded at the "neither a name nor a phone" check, so the request did nothing, silently.


def test_the_reported_note_instruction_is_returned():
    result = extract_customer_update(
        "Please add a note for this customer: He prefers green tea over coffee"
    )

    assert result is not None
    assert result.note == "He prefers green tea over coffee"
    assert result.full_name is None
    assert result.phone_number is None
    assert not result.is_empty


@pytest.mark.parametrize(
    ("message", "note"),
    [
        ("add a note: He prefers green tea", "He prefers green tea"),
        ("make a note - she hates loud prints", "she hates loud prints"),
        ("note that he dislikes wool", "he dislikes wool"),
        ("note down: birthday is in March", "birthday is in March"),
        ("remember that she asked for a callback", "she asked for a callback"),
        ("keep a note: prefers WhatsApp", "prefers WhatsApp"),
        ("please note the customer is allergic to wool", "the customer is allergic to wool"),
        ("Please note: 'that he likes pink'", "he likes pink"),
        # "for this customer" / "about <name>" name the target, not the note.
        ("add a note about Kasha: she loves silk", "she loves silk"),
        ("add a note for the client: he is moving abroad", "he is moving abroad"),
        # No colon: the value is what follows the instruction phrase.
        ("add a note he prefers green tea", "he prefers green tea"),
    ],
)
def test_note_text_is_extracted(message, note):
    result = extract_customer_update(message)

    assert result is not None
    assert result.note == note
    assert result.full_name is None
    assert result.phone_number is None


def test_a_note_with_neither_colon_nor_text_is_not_an_instruction():
    assert extract_customer_update("please add a note for this customer") is None


def test_a_name_or_phone_inside_a_note_is_not_read_as_an_identity_update():
    """A note is prose the staff member wrote: its words are not instructions to rewrite a record."""
    name_note = extract_customer_update("add a note: she asked about the name of a fabric")
    assert name_note is not None
    assert name_note.full_name is None
    assert name_note.note == "she asked about the name of a fabric"

    phone_note = extract_customer_update("add a note: call her back on 0771234567")
    assert phone_note is not None
    assert phone_note.phone_number is None
    assert phone_note.note == "call her back on 0771234567"


def test_an_identity_instruction_and_a_note_can_share_one_message():
    result = extract_customer_update(
        "Please update this customer with the name Kasha Vivian Perera and number 0771234567 "
        "and add a note: she prefers green tea"
    )

    assert result is not None
    assert result.full_name == "Kasha Vivian Perera"
    assert result.phone_number == "0771234567"
    assert result.note == "she prefers green tea"


def test_identity_instructions_are_unchanged_and_carry_no_note():
    result = extract_customer_update(
        "Please update this customer with the name Kasha Vivian Perera and number 0771234567"
    )

    assert result is not None
    assert result.note is None
