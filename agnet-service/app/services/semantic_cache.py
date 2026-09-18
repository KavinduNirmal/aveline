"""Semantic cache for LLM responses.

Prevents duplicate LLM calls for repeated queries. Responses are keyed by a hash
of (system_prompt + prompt + model) so a change in any of them produces a fresh
cache entry. This is the most valuable cache for cost reduction.
"""

import hashlib
import logging

import redis.asyncio as aioredis

logger = logging.getLogger("aveline.agent.semantic_cache")

_PREFIX = "semantic_cache:"


class SemanticCache:
    """Redis-backed cache of LLM responses keyed by prompt + system + model."""

    def __init__(self, redis: aioredis.Redis, ttl: int = 3600) -> None:
        self._redis = redis
        self._ttl = ttl

    def _build_key(self, prompt: str, system_prompt: str, model: str) -> str:
        """Return a deterministic cache key for the given inputs."""
        payload = f"{system_prompt}::{prompt}::{model}"
        digest = hashlib.sha256(payload.encode()).hexdigest()
        return f"{_PREFIX}{digest}"

    async def get(self, prompt: str, system_prompt: str, model: str) -> str | None:
        """Return the cached response for the inputs, or ``None`` on a miss."""
        key = self._build_key(prompt, system_prompt, model)
        return await self._redis.get(key)

    async def set(
        self,
        prompt: str,
        system_prompt: str,
        model: str,
        response: str,
        ttl: int | None = None,
    ) -> None:
        """Cache ``response`` for the inputs with an optional TTL override."""
        key = self._build_key(prompt, system_prompt, model)
        await self._redis.set(key, response, ex=ttl or self._ttl)

    async def invalidate(self) -> None:
        """Delete every semantic cache entry."""
        keys = [key async for key in self._redis.scan_iter(match=f"{_PREFIX}*")]
        if keys:
            await self._redis.delete(*keys)
            logger.debug("Invalidated %d semantic cache entries.", len(keys))
