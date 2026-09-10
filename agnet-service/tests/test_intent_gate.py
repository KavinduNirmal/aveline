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
