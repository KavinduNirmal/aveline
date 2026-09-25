import asyncio
from uuid import uuid4

import fakeredis.aioredis
import pytest

from app.events.bus import RedisEventBus, channel_for, pattern_for
from app.events.schemas import EventEnvelope


def test_channel_for_builds_org_scoped_channel():
    org_id = uuid4()

    assert channel_for(org_id, "message.received") == f"aveline:{org_id}:message.received"


def test_pattern_for_matches_any_organization():
    assert pattern_for("workflow.completed") == "aveline:*:workflow.completed"


def test_envelope_defaults_generate_id_and_timestamp():
    envelope = EventEnvelope(event_type="message.received")

    assert envelope.event_id is not None
    assert envelope.timestamp is not None
    assert envelope.org_id is None
    assert envelope.payload is None


def test_envelope_serializes_to_snake_case_json():
    envelope = EventEnvelope(event_type="message.received", org_id=uuid4(), payload={"customerId": "c-1"})

    data = envelope.model_dump_json()

    assert '"event_id"' in data
    assert '"event_type"' in data
    assert '"org_id"' in data
    assert '"trace_id"' in data
    assert '"payload"' in data


@pytest.mark.asyncio
async def test_publish_and_listen_delivers_to_registered_handler():
    redis = fakeredis.aioredis.FakeRedis(decode_responses=True)
    bus = RedisEventBus(redis)
    received: list[EventEnvelope] = []
    done = asyncio.Event()

    async def handler(envelope: EventEnvelope) -> None:
        received.append(envelope)
        done.set()

    bus.register("message.received", handler)
    await bus.start(["message.received"])

    org_id = uuid4()
    await bus.publish("message.received", org_id, {"text": "hi"})

    await asyncio.wait_for(done.wait(), timeout=2.0)
    await bus.stop()

    assert len(received) == 1
    assert received[0].event_type == "message.received"
    assert received[0].org_id == org_id
    assert received[0].payload == {"text": "hi"}


@pytest.mark.asyncio
async def test_dispatch_malformed_message_is_skipped():
    redis = fakeredis.aioredis.FakeRedis(decode_responses=True)
    bus = RedisEventBus(redis)
    invoked = False

    async def handler(envelope: EventEnvelope) -> None:
        nonlocal invoked
        invoked = True

    bus.register("message.received", handler)

    await bus._dispatch("aveline:org:message.received", "not json")

    assert not invoked


@pytest.mark.asyncio
async def test_dispatch_handler_exception_does_not_abort_other_handlers():
    redis = fakeredis.aioredis.FakeRedis(decode_responses=True)
    bus = RedisEventBus(redis)
    second_invoked = False

    async def failing(envelope: EventEnvelope) -> None:
        raise RuntimeError("boom")

    async def second(envelope: EventEnvelope) -> None:
        nonlocal second_invoked
        second_invoked = True

    bus.register("workflow.completed", failing)
    bus.register("workflow.completed", second)

    envelope = EventEnvelope(event_type="workflow.completed")
    await bus._dispatch("aveline:org:workflow.completed", envelope.model_dump_json())

    assert second_invoked


@pytest.mark.asyncio
async def test_unregister_removes_handler():
    redis = fakeredis.aioredis.FakeRedis(decode_responses=True)
    bus = RedisEventBus(redis)
    invoked = False

    async def handler(envelope: EventEnvelope) -> None:
        nonlocal invoked
        invoked = True

    bus.register("message.received", handler)
    bus.unregister("message.received", handler)

    envelope = EventEnvelope(event_type="message.received")
    await bus._dispatch("aveline:org:message.received", envelope.model_dump_json())

    assert not invoked


@pytest.mark.asyncio
async def test_ping_returns_true_when_redis_reachable():
    redis = fakeredis.aioredis.FakeRedis(decode_responses=True)
    bus = RedisEventBus(redis)

    assert await bus.ping() is True
