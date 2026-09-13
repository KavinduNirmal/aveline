"""Unit tests for VisualIntentGate classification (TDD)."""

import pytest

from app.agents.visual_insight.intent_gate import VisualIntentGate


@pytest.mark.parametrize(
    "message,context,expected",
    [
        (
            "Find me a green saree",
            {},
            "item_search",
        ),
        (
            "Build a wedding outfit",
            {},
            "outfit_composition",
        ),
        (
            "Analyze this photo",
            {},
            "image_analysis",
        ),
        (
            "Can you get this?",
            {"has_reference_image": True},
            "image_analysis",
        ),
    ],
)
def test_visual_intent_gate(
    message,
    context,
    expected,
):
    gate = VisualIntentGate()

    result = gate.infer(
        message,
        context,
    )

    assert result["intent_type"] == expected


def test_reference_image_has_highest_priority():
    gate = VisualIntentGate()

    result = gate.infer(
        "Find me an outfit like this",
        {
            "has_reference_image": True,
        },
    )

    assert result["intent_type"] == "image_analysis"


def test_image_keyword_takes_precedence_over_outfit_and_item_search():
    gate = VisualIntentGate()

    result = gate.infer(
        "Find me an outfit like this image",
        {},
    )

    assert result["intent_type"] == "image_analysis"


def test_outfit_keyword_takes_precedence_over_item_search():
    gate = VisualIntentGate()

    result = gate.infer(
        "Find me a saree outfit for a wedding",
        {},
    )

    assert result["intent_type"] == "outfit_composition"

