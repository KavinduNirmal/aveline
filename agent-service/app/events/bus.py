"""Async Redis Pub/Sub event bus for the agent service (ADR-014).

Channels are org-scoped: ``aveline:<org_id>:<event_type>``. Because a single agent
deployment serves every organization, the subscriber listens on the pattern
``aveline:*:<event_type>`` (Redis ``PSUBSCRIBE``) while the channel still carries the org
for routing and audit.
"""

import asyncio
import logging
from collections import defaultdict
from collections.abc import Awaitable, Callable
from typing import Any
from uuid import UUID

import redis.asyncio as aioredis
from pydantic import ValidationError

from app.events.schemas import EventEnvelope

logger = logging.getLogger("aveline.agent.events")

Handler = Callable[[EventEnvelope], Awaitable[None]]

CHANNEL_PREFIX = "aveline"
ALL_ORGANIZATIONS = "*"


def channel_for(org_id: UUID | str, event_type: str) -> str:
    """Build the publish channel for a single organization."""
    return f"{CHANNEL_PREFIX}:{org_id}:{event_type}"


def pattern_for(event_type: str) -> str:
    """Build the subscribe pattern matching an event type for every organization."""
    return f"{CHANNEL_PREFIX}:{ALL_ORGANIZATIONS}:{event_type}"


class RedisEventBus:
    """Async Redis Pub/Sub event bus.

    Publishing is fire-and-forget: if no subscriber is connected the message is dropped.
    Critical transactional operations must not rely solely on the bus and should use the
    internal HTTP path (ADR-009) as a fallback.
    """

    def __init__(self, redis: aioredis.Redis) -> None:
        self._redis = redis
        self._handlers: dict[str, list[Handler]] = defaultdict(list)
        self._listener_task: asyncio.Task | None = None
        self._pubsub: aioredis.client.PubSub | None = None

    async def ping(self) -> bool:
        """Return whether Redis is reachable (used by the health endpoint)."""
        try:
            return bool(await self._redis.ping())
        except Exception:  # noqa: BLE001 - health probe must not raise
            return False

    async def publish(
        self,
        event_type: str,
        org_id: UUID | str,
        payload: dict[str, Any] | None = None,
        trace_id: UUID | None = None,
    ) -> None:
        """Publish an event to ``aveline:<org_id>:<event_type>``."""
        envelope = EventEnvelope(event_type=event_type, org_id=UUID(str(org_id)), payload=payload, trace_id=trace_id)
        channel = channel_for(org_id, event_type)
        await self._redis.publish(channel, envelope.model_dump_json())
        logger.debug("Published event %s to channel %s", event_type, channel)

    def register(self, event_type: str, handler: Handler) -> None:
        """Register a handler invoked for every envelope of the given event type."""
        self._handlers[event_type].append(handler)

    def unregister(self, event_type: str, handler: Handler) -> None:
        """Remove a previously registered handler."""
        handlers = self._handlers.get(event_type, [])
        if handler in handlers:
            handlers.remove(handler)
        if not handlers:
            self._handlers.pop(event_type, None)

    async def start(self, event_types: list[str]) -> None:
        """Subscribe to the given event types and begin listening in a background task."""
        if self._listener_task is not None:
            return

        patterns = [pattern_for(event_type) for event_type in event_types]
        if not patterns:
            logger.info("No event types configured for subscription; event listener is idle.")
            return

        self._pubsub = self._redis.pubsub()
        await self._pubsub.psubscribe(*patterns)
        logger.info("Subscribed to Redis event patterns: %s", ", ".join(patterns))

        self._listener_task = asyncio.create_task(self._run())

    async def stop(self) -> None:
        """Cancel the background listener and close the pubsub connection."""
        if self._listener_task is not None:
            self._listener_task.cancel()
            try:
                await self._listener_task
            except asyncio.CancelledError:
                pass
            self._listener_task = None
        if self._pubsub is not None:
            await self._pubsub.aclose()
            self._pubsub = None

    async def _run(self) -> None:
        try:
            async for message in self._pubsub.listen():
                if message.get("type") == "pmessage":
                    await self._dispatch(message["channel"], message["data"])
        except asyncio.CancelledError:
            logger.info("Event listener stopped.")
            raise

    async def _dispatch(self, channel: str, message: Any) -> None:
        try:
            envelope = EventEnvelope.model_validate_json(message)
        except (ValidationError, TypeError, ValueError):
            logger.warning("Skipping malformed event message on channel %s.", channel)
            return

        handlers = list(self._handlers.get(envelope.event_type, []))
        if not handlers:
            logger.debug("No handlers registered for event type %s; message skipped.", envelope.event_type)
            return

        for handler in handlers:
            try:
                await handler(envelope)
            except Exception:  # noqa: BLE001 - a handler failure must not abort the listener
                logger.exception("Event handler failed for event type %s.", envelope.event_type)
