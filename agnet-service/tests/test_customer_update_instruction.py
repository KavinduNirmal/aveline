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
        # Degenerate input.
        "",
        "   ",
    ],
)
def test_is_not_an_update_instruction(message):
    assert extract_customer_update(message) is None


def test_an_instruction_stating_nothing_usable_is_not_an_instruction():
    # "update" with neither a name nor a phone gives nothing to apply.
    assert extract_customer_update("please update this customer") is None


def test_none_message_is_safe():
    assert extract_customer_update(None) is None
