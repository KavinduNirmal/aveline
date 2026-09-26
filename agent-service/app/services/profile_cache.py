"""Profile cache for customer context.

Caches customer profiles/memories as JSON to avoid repeated backend lookups
during a conversation. Keyed by customer id with a short TTL.
"""

import json
import logging
from typing import Any

import redis.asyncio as aioredis

logger = logging.getLogger("aveline.agent.profile_cache")

_PREFIX = "customer_profile:"


class ProfileCache:
    """Redis-backed cache of customer profiles keyed by customer id."""

    def __init__(self, redis: aioredis.Redis, ttl: int = 300) -> None:
        self._redis = redis
        self._ttl = ttl

    def _key(self, customer_id: str) -> str:
        return f"{_PREFIX}{customer_id}"

    async def get_customer_profile(self, customer_id: str) -> dict[str, Any] | None:
        """Return the cached profile for ``customer_id``, or ``None`` on a miss."""
        cached = await self._redis.get(self._key(customer_id))
        if cached is None:
            return None
        return json.loads(cached)

    async def set_customer_profile(self, customer_id: str, profile: dict[str, Any]) -> None:
        """Cache ``profile`` for ``customer_id`` as JSON with the configured TTL."""
        await self._redis.set(self._key(customer_id), json.dumps(profile), ex=self._ttl)
