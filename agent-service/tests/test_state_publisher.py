"""Tests for agent lifecycle state publishing (app/events/state_publisher.py).

The concierge workflow emits ``agent.status`` events as it progresses so the API can
broadcast Aveline's state to connected clients over SignalR.
"""

from uuid import uuid4

import pytest

from app.events.state_publisher import AGENT_STATUS, publish_agent_state
from app.schemas.state import AgentState


@pytest.mark.asyncio
async def test_publish_agent_state_publishes_status_event():
    class FakeBus:
        def __init__(self):
            self.published: list[tuple[str, object, dict]] = []

        async def publish(self, event_type, org_id, payload=None, trace_id=None):
            self.published.append((event_type, org_id, payload, trace_id))

    bus = FakeBus()
    org_id = uuid4()
    thread_id = "thread-1"

    await publish_agent_state(bus, org_id, thread_id, AgentState.searching)

    assert len(bus.published) == 1
    event_type, published_org, payload, trace_id = bus.published[0]
    assert event_type == AGENT_STATUS
    assert published_org == org_id
    assert payload["thread_id"] == thread_id
    assert payload["state"] == "searching"


@pytest.mark.asyncio
async def test_publish_agent_state_carries_agent_key_and_trace_id():
    class FakeBus:
        def __init__(self):
            self.published = []

        async def publish(self, event_type, org_id, payload=None, trace_id=None):
            self.published.append((payload, trace_id))

    bus = FakeBus()
    trace_id = uuid4()

    await publish_agent_state(bus, uuid4(), "thread-2", AgentState.tool_call, agent_key="lina", trace_id=trace_id)

    payload, published_trace = bus.published[0]
    assert payload["agent_key"] == "lina"
    assert published_trace == trace_id


@pytest.mark.asyncio
async def test_publish_agent_state_omits_agent_key_when_absent():
    class FakeBus:
        def __init__(self):
            self.published = []

        async def publish(self, event_type, org_id, payload=None, trace_id=None):
            self.published.append(payload)

    bus = FakeBus()
    await publish_agent_state(bus, uuid4(), "thread-3", AgentState.thinking)

    assert "agent_key" not in bus.published[0]
