"""Tool result cache.

Caches results of backend tool calls (e.g. inventory searches) to avoid repeated
backend API calls. Keys are built from canonicalized criteria so equivalent
criteria in different key orders share a cache entry.
"""

import hashlib
import json
import logging
from typing import Any

import redis.asyncio as aioredis

logger = logging.getLogger("aveline.agent.tool_cache")

_PREFIX = "tool_cache:"


class ToolCache:
    """Redis-backed cache of tool results keyed by canonicalized criteria."""

    def __init__(self, redis: aioredis.Redis, ttl: int = 60) -> None:
        self._redis = redis
        self._ttl = ttl

    def _build_key(self, criteria: dict[str, Any]) -> str:
        """Return a deterministic key for ``criteria`` regardless of key order."""
        canonical = json.dumps(criteria, sort_keys=True, default=str)
        digest = hashlib.sha256(canonical.encode()).hexdigest()
        return f"{_PREFIX}{digest}"

    async def get_inventory_search(self, criteria: dict[str, Any]) -> list[Any] | None:
        """Return cached inventory results for ``criteria``, or ``None`` on a miss."""
        cached = await self._redis.get(self._build_key(criteria))
        if cached is None:
            return None
        return json.loads(cached)

    async def set_inventory_search(self, criteria: dict[str, Any], results: list[Any]) -> None:
        """Cache ``results`` for ``criteria`` with the configured TTL."""
        await self._redis.set(self._build_key(criteria), json.dumps(results), ex=self._ttl)
