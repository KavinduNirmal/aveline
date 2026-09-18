"""Tests for runtime output schema coercion (Issue #167).

``coerce_output`` validates the assembled ``MemoryAgentOutput``-shaped dict in the running path
so contract drift between the agent and the backend fails loudly (extra fields forbidden) with a
graceful ``None`` instead of an uncaught exception.
"""


from app.agents.customer_memory.nodes import coerce_output


def test_valid_output_coerces_and_round_trips():
    payload = {
        "status": "success",
        "parsed_intent": {"intent_type": "item_search", "occasion": "wedding", "color": "blue"},
        "customer": {"customer_id": "cust-1", "phone_number": "+94771234567", "status": "vip"},
        "extracted_memories": [{"content": "Prefers silk", "category": "preference", "is_explicit": True}],
        "detected_events": [{"event_type": "wedding", "event_date": "2026-12-01"}],
        "interaction_brief": "Sarah Perera (vip)",
        "draft_response": "Hi Sarah!",
    }
    model = coerce_output(payload)
    assert model is not None
    assert model.status == "success"
    assert model.customer is not None and model.customer.customer_id == "cust-1"
    assert model.detected_events[0].event_type == "wedding"


def test_output_without_phone_is_accepted():
    """The agent can know a customer by id/name without a phone number."""
    payload = {
        "status": "success",
        "parsed_intent": {"intent_type": "general_inquiry"},
        "customer": {"customer_id": "cust-1", "status": "returning"},
    }
    assert coerce_output(payload) is not None


def test_invalid_status_returns_none():
    payload = {"status": "exploded"}
    assert coerce_output(payload) is None


def test_extra_unknown_field_returns_none():
    payload = {
        "status": "success",
        "parsed_intent": {"intent_type": "general_inquiry"},
        "bogus": "x",
    }
    assert coerce_output(payload) is None


def test_detected_event_without_date_returns_none():
    """A structured output event must carry a date; undated events are not structured."""
    payload = {
        "status": "success",
        "detected_events": [{"event_type": "wedding"}],  # missing required event_date
    }
    assert coerce_output(payload) is None
