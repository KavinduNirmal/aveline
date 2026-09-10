"""Tests for persona-attributed message publishing (app/events/message_publisher.py).

The concierge workflow produces a result; the publisher turns it into ``message.created``
events attributed to the personas that produced content:

- **Aveline** (the orchestrator) always emits a summary ``Note``.
- **Ava** (memory) emits only when the memory agent produced real customer content.
- **Elle**/**Lina** emit only when their output carries content (their stubs are wired by
  Issue #151); a bare ``{"ran": true}`` produces no message.
"""

from uuid import uuid4

import pytest

from app.events.message_publisher import build_agent_messages, publish_agent_messages
from app.schemas.response import AgentResponse, AgentStatus


def _memory_with_customer() -> dict:
    return {
        "agent": "memory",
        "ran": True,
        "status": "success",
        "customer": {"customer_id": "c1", "full_name": "Michael", "status": "returning"},
        "parsed_intent": {"intent_type": "item_search"},
        "interaction_brief": "Michael (returning)",
        "extracted_memories": [
            {"content": "Michael prefers silk dresses", "category": "preference", "is_explicit": True, "confidence": 0.9},
            {"content": "Michael has a wedding on 2026-12-01", "category": "event", "is_explicit": True, "confidence": 0.9},
        ],
        "detected_events": [],
        "draft_response": "Hi Michael! We would love to help you find a silk option.",
        "action_required": "send_whatsapp",
    }


def _result_with_memory() -> AgentResponse:
    return AgentResponse(
        status=AgentStatus.success,
        output={
            "intent": "item_search",
            "memory": _memory_with_customer(),
            "visual": {"agent": "visual", "ran": True},
            "commerce": None,
        },
    )


def _result_memory_skipped() -> AgentResponse:
    return AgentResponse(
        status=AgentStatus.success,
        output={
            "intent": "general_inquiry",
            "memory": {
                "agent": "memory",
                "ran": True,
                "status": "skipped",
                "reason": "no customer context available",
            },
            "visual": None,
            "commerce": None,
        },
    )


def test_build_agent_messages_always_includes_aveline_summary():
    messages = build_agent_messages(_result_with_memory())

    assert any(m["author"]["agent_key"] == "aveline" for m in messages)
    assert all(m["kind"] == "Note" for m in messages)
    assert all("blocks" in m for m in messages)


def test_build_agent_messages_attributes_ava_when_it_produced_real_content():
    messages = build_agent_messages(_result_with_memory())

    ava = [m for m in messages if m["author"]["agent_key"] == "ava"]
    assert len(ava) == 1
    # Real memory content is surfaced: brief + at_a_glance memories + draft suggestion.
    block_types = [b["type"] for b in ava[0]["blocks"]]
    assert "text" in block_types
    assert "at_a_glance" in block_types
    assert "suggestion" in block_types


def test_build_agent_messages_does_not_emit_placeholder_specialist_text():
    messages = build_agent_messages(_result_with_memory())
    text = " ".join(
        block.get("text", "")
        for m in messages
        for block in m["blocks"]
        if isinstance(block, dict) and "text" in block
    )

    assert "has reviewed this request" not in text


def test_build_agent_messages_specialist_without_content_is_silent():
    # Visual "ran" but produced no content -> Elle must not post a message yet (Issue #151).
    messages = build_agent_messages(_result_with_memory())
    keys = {m["author"]["agent_key"] for m in messages}

    assert "elle" not in keys


def test_build_agent_messages_attributes_elle_when_visual_produced_content():
    result = AgentResponse(
        status=AgentStatus.success,
        output={
            "intent": "item_search",
            "memory": None,
            "visual": {
                "agent": "visual",
                "ran": True,
                "status": "success",
                "suggestion": "These pieces match the request.",
                "items": [
                    {"itemId": "i1", "name": "Silk Slip Dress", "price": 24000, "size": "M", "stock": 2}
                ],
            },
            "commerce": None,
        },
    )
    messages = build_agent_messages(result)

    elle = [m for m in messages if m["author"]["agent_key"] == "elle"]
    assert len(elle) == 1
    assert any(b["type"] == "piece" for b in elle[0]["blocks"])
    assert any(b["type"] == "suggestion" for b in elle[0]["blocks"])


def test_build_agent_messages_memory_skipped_does_not_emit_ava():
    messages = build_agent_messages(_result_memory_skipped())
    keys = {m["author"]["agent_key"] for m in messages}

    assert keys == {"aveline"}


def test_build_agent_messages_out_of_scope_only_aveline():
    result = AgentResponse(status=AgentStatus.out_of_scope, output={"reason": "outside domain"})
    messages = build_agent_messages(result)

    assert len(messages) == 1
    assert messages[0]["author"]["agent_key"] == "aveline"


def test_build_agent_messages_carries_thread_id_and_workflow_run_id():
    thread_id = "thread-abc"
    run_id = uuid4()
    messages = build_agent_messages(_result_with_memory(), thread_id=thread_id, workflow_run_id=run_id)

    for m in messages:
        assert m["thread_id"] == thread_id
        assert m["workflow_run_id"] == str(run_id)


@pytest.mark.asyncio
async def test_publish_agent_messages_publishes_one_event_per_message():
    class FakeBus:
        def __init__(self):
            self.published: list[tuple[str, object, dict]] = []

        async def publish(self, event_type, org_id, payload=None, trace_id=None):
            self.published.append((event_type, org_id, payload))

    bus = FakeBus()
    org_id = uuid4()
    thread_id = "thread-1"

    await publish_agent_messages(bus, org_id, thread_id, _result_with_memory())

    assert len(bus.published) >= 2
    for event_type, published_org, payload in bus.published:
        assert event_type == "message.created"
        assert published_org == org_id
        assert payload["thread_id"] == thread_id
