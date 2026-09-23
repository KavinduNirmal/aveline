"""Tests for the shared Intent Gate (app/gate.py).

The gate classifies inbound messages and routes them to agents. It is hybrid:
deterministic keyword rules first, with an optional LLM fallback for ambiguous
input. The gate is shared infra and must not execute slice business logic.
"""

import pytest

from app.gate import IntentGateOutput, classify_by_rules, infer_intent

# ---------------------------------------------------------------------------
# classify_by_rules (deterministic)
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "message,expected",
    [
        ("Do you have a blue saree for a wedding?", "item_search"),
        ("I need a party dress in my size", "item_search"),
        ("What blouses do you have?", "item_search"),
        ("How much is this dress?", "pricing_query"),
        ("Can you give me a discount?", "pricing_query"),
        ("What is the cost of the blouse?", "pricing_query"),
        ("Remember she prefers pastel colors", "customer_preference"),
        ("She prefers silk over cotton", "customer_preference"),
        ("We have a wedding event on Saturday", "event_query"),
        ("Her birthday is next month", "event_query"),
    ],
)
def test_classify_by_rules_known_intents(message, expected):
    result = classify_by_rules(message)
    assert result.intent_type == expected
    assert result.is_relevant is True


def test_classify_by_rules_general_inquiry_fallback():
    result = classify_by_rules("Hello, how are you today?")
    assert result.intent_type == "general_inquiry"


def test_classify_by_rules_out_of_scope():
    result = classify_by_rules("Write me a python script to sort a list")
    assert result.intent_type == "out_of_scope"
    assert result.is_relevant is False


def test_classify_by_rules_item_search_suggests_memory_and_visual():
    result = classify_by_rules("Do you have a blue saree?")
    assert set(result.suggested_agents) == {"memory", "visual"}


def test_classify_by_rules_pricing_suggests_memory_and_commerce():
    result = classify_by_rules("How much is this dress?")
    assert set(result.suggested_agents) == {"memory", "commerce"}


def test_classify_by_rules_out_of_scope_has_no_agents():
    result = classify_by_rules("Tell me a joke")
    assert result.suggested_agents == []


def test_classify_by_rules_safety_flags_default_empty():
    result = classify_by_rules("Do you have a dress?")
    assert result.safety_flags == []
    assert result.requires_approval is False


# ---------------------------------------------------------------------------
# Known keyword gaps (pinned, not yet fixed)
# ---------------------------------------------------------------------------
#
# ``item_search`` recognises a hand-written vocabulary: dress, saree, blouse, outfit, party,
# bluish, size, stock, inventory, item, photo, image, picture, matching. Fashion vocabulary
# outside that list falls through to ``general_inquiry``, whose routing is ``["memory"]`` — so
# the Visual agent never runs for a genuine product-search question.
#
# Note the inconsistency that makes the gap obvious: ``bluish`` is present while ``pinkish``
# is not. Colours were added one at a time rather than as a class.
#
# Pinned with ``xfail(strict=True)`` so that when the vocabulary is generalised (or the
# supervisor takes over routing) these turn into XPASS and force a deliberate update here.
# See ADR-023 (Decision 3) and the implementation plan (W0.4).


@pytest.mark.xfail(
    strict=True,
    reason="Keyword gap: 'gowns' is not in the item_search vocabulary (ADR-023, W0.4).",
)
def test_gown_is_recognised_as_item_search():
    result = classify_by_rules("Hello there. Are there any pinkish gowns in your collection?")
    assert result.intent_type == "item_search"
    assert "visual" in result.suggested_agents


@pytest.mark.xfail(
    strict=True,
    reason="Keyword gap: 'pinkish' is absent from the item_search vocabulary (ADR-023, W0.4).",
)
def test_pinkish_is_recognised_as_item_search():
    assert classify_by_rules("Do you have anything pinkish?").intent_type == "item_search"


def test_bluish_is_already_recognised_documenting_the_asymmetry():
    """The asymmetry that proves the vocabulary was grown one word at a time.

    ``bluish`` is in ``_RULE_KEYWORDS``; ``pinkish`` is not. This test passes today, and exists
    so the inconsistency between it and the failing case above is visible in one place rather
    than inferred.
    """
    assert classify_by_rules("Do you have anything bluish?").intent_type == "item_search"


# ---------------------------------------------------------------------------
# infer_intent (async, hybrid)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_infer_intent_uses_rules_when_confident():
    result = await infer_intent("Do you have a blue saree for a wedding?")
    assert result.intent_type == "item_search"


@pytest.mark.asyncio
async def test_infer_intent_does_not_call_llm_when_confident():
    called = {"n": 0}

    async def llm_classifier(message):
        called["n"] += 1
        return IntentGateOutput(intent_type="pricing_query", suggested_agents=["commerce"])

    result = await infer_intent("How much is this dress?", llm_classifier=llm_classifier)
    assert result.intent_type == "pricing_query"
    assert called["n"] == 0


@pytest.mark.asyncio
async def test_infer_intent_calls_llm_on_ambiguous():
    called = {"n": 0}

    async def llm_classifier(message):
        called["n"] += 1
        return IntentGateOutput(intent_type="pricing_query", suggested_agents=["commerce"])

    result = await infer_intent("Can you help me with something?", llm_classifier=llm_classifier)
    assert called["n"] == 1
    assert result.intent_type == "pricing_query"


@pytest.mark.asyncio
async def test_infer_intent_keeps_rules_when_llm_returns_none():
    async def llm_classifier(message):
        return None

    result = await infer_intent("Hello there", llm_classifier=llm_classifier)
    assert result.intent_type == "general_inquiry"


@pytest.mark.asyncio
async def test_infer_intent_no_llm_returns_general_inquiry():
    result = await infer_intent("Just saying hi")
    assert result.intent_type == "general_inquiry"
