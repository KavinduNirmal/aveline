"""Tests for the Customer Memory Agent schemas (app/schemas/customer_memory.py).

Verifies the typed I/O contract: structured intent, extracted memories, detected events,
profile summary, and the top-level MemoryAgentOutput. Each model forbids extra fields so
contract drift between the agent and the backend fails loudly.
"""

import pytest
from pydantic import ValidationError

from app.schemas.customer_memory import (
    DetectedEvent,
    ExtractedMemory,
    MemoryAgentOutput,
    ParsedIntent,
)


def test_parsed_intent_accepts_wedding_inquiry():
    intent = ParsedIntent(
        intent_type="item_search",
        occasion="wedding",
        color="blue",
        size="M",
        budget=45000,
    )
    assert intent.intent_type == "item_search"
    assert intent.occasion == "wedding"
    assert intent.color == "blue"


def test_parsed_intent_rejects_unknown_intent_type():
    with pytest.raises(ValidationError):
        ParsedIntent(intent_type="buy_house")


def test_parsed_intent_rejects_extra_fields():
    with pytest.raises(ValidationError):
        ParsedIntent(intent_type="item_search", bogus="value")


def test_extracted_memory_validates_confidence_bounds():
    with pytest.raises(ValidationError):
        ExtractedMemory(content="Prefers silk", category="preference", confidence=1.5)


def test_extracted_memory_defaults():
    memory = ExtractedMemory(content="She likes pastels")
    assert memory.category == "fact"
    assert memory.is_explicit is False
    assert memory.confidence == 0.50


def test_detected_event_requires_date():
    with pytest.raises(ValidationError):
        DetectedEvent(event_type="wedding")


def test_detected_event_accepts_valid():
    event = DetectedEvent(event_type="wedding", event_date="2026-12-01", description="Sister's wedding")
    assert event.event_type == "wedding"
    assert event.description == "Sister's wedding"


def test_memory_agent_output_rejects_extra_fields():
    with pytest.raises(ValidationError):
        MemoryAgentOutput(status="success", unexpected="x")


def test_memory_agent_output_status_must_be_known():
    with pytest.raises(ValidationError):
        MemoryAgentOutput(status="exploded")


def test_memory_agent_output_round_trips_full_result():
    output = MemoryAgentOutput(
        status="success",
        parsed_intent=ParsedIntent(intent_type="item_search", color="blue"),
        extracted_memories=[ExtractedMemory(content="Sarah has a wedding", category="event")],
        detected_events=[DetectedEvent(event_type="wedding", event_date="2026-09-12")],
        draft_response="Hi Sarah! We have a beautiful blue saree.",
        action_required="send_whatsapp",
    )
    data = output.model_dump()
    assert data["status"] == "success"
    assert data["extracted_memories"][0]["category"] == "event"
    assert data["detected_events"][0]["event_type"] == "wedding"
