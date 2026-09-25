"""Redis-backed cache service.

Used for semantic caching (to reduce LLM calls) and as a thin wrapper over the
shared Redis connection for pub/sub. All operations are async.
"""

import logging

import redis.asyncio as aioredis

logger = logging.getLogger("aveline.agent.cache")


class CacheService:
    """Async Redis cache with get/set (TTL) and publish helpers."""

    def __init__(self, redis: aioredis.Redis) -> None:
        self._redis = redis

    async def get(self, key: str) -> str | None:
        """Return the string value for ``key``, or ``None`` if absent."""
        return await self._redis.get(key)

    async def set(self, key: str, value: str, ttl: int = 300) -> None:
        """Store ``value`` under ``key`` with an optional TTL in seconds."""
        await self._redis.set(key, value, ex=ttl)

    async def publish(self, channel: str, message: str) -> None:
        """Publish ``message`` to ``channel`` (fire-and-forget)."""
        await self._redis.publish(channel, message)
