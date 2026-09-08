"""Tests for persona-attributed message publishing (app/events/message_publisher.py).

The concierge workflow produces a result; the publisher turns it into one or more
``message.created`` events attributed to the personas that participated (Aveline the
orchestrator always summarizes; Ava/Elle/Lina each get a message when their agent ran).
"""

from uuid import uuid4

import pytest

from app.events.message_publisher import build_agent_messages, publish_agent_messages
from app.schemas.response import AgentResponse, AgentStatus


def _success_result() -> AgentResponse:
    return AgentResponse(
        status=AgentStatus.success,
        output={
            "intent": "item_search",
            "memory": {"agent": "memory", "ran": True},
            "visual": {"agent": "visual", "ran": True},
            "commerce": None,
        },
    )


def test_build_agent_messages_always_includes_aveline_summary():
    messages = build_agent_messages(_success_result())

    assert any(m["author"]["agent_key"] == "aveline" for m in messages)
    assert all(m["kind"] == "Note" for m in messages)
    assert all("blocks" in m for m in messages)


def test_build_agent_messages_attributes_specialists_that_ran():
    messages = build_agent_messages(_success_result())
    keys = {m["author"]["agent_key"] for m in messages}

    # Memory and visual ran -> Ava and Elle appear. Commerce did not -> Lina absent.
    assert "ava" in keys
    assert "elle" in keys
    assert "lina" not in keys


def test_build_agent_messages_out_of_scope_only_aveline():
    result = AgentResponse(status=AgentStatus.out_of_scope, output={"reason": "outside domain"})
    messages = build_agent_messages(result)

    assert len(messages) == 1
    assert messages[0]["author"]["agent_key"] == "aveline"


def test_build_agent_messages_carries_thread_id_and_workflow_run_id():
    thread_id = "thread-abc"
    run_id = uuid4()
    messages = build_agent_messages(_success_result(), thread_id=thread_id, workflow_run_id=run_id)

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

    await publish_agent_messages(bus, org_id, thread_id, _success_result())

    assert len(bus.published) >= 2
    for event_type, published_org, payload in bus.published:
        assert event_type == "message.created"
        assert published_org == org_id
        assert payload["thread_id"] == thread_id
