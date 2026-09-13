"""Product Analysis Cache for visual feature extraction results.

Caches visual attributes extracted from product images in Redis with a 1-hour
TTL (3600 seconds) to avoid redundant multimodal vision API calls.
"""

import json
import logging
from typing import Any

logger = logging.getLogger("aveline.agent.product_analysis_cache")

_PREFIX = "product_analysis:"
_DEFAULT_TTL = 3600  # 1 hour


class ProductAnalysisCache:
    """Redis-backed cache for visual product analysis results."""

    def __init__(self, redis: Any, ttl: int = _DEFAULT_TTL) -> None:
        self._redis = redis
        self._ttl = ttl

    @property
    def ttl(self) -> int:
        """Configured TTL in seconds (default: 3600s / 1 hour)."""
        return self._ttl

    def _key(self, image_url: str) -> str:
        return f"{_PREFIX}{image_url}"

    async def get(self, image_url: str) -> dict[str, Any] | None:
        """Return cached analysis attributes for ``image_url`` or ``None`` on miss."""
        cached = await self._redis.get(self._key(image_url))
        if cached is None:
            return None
        if isinstance(cached, bytes):
            cached = cached.decode("utf-8")
        if isinstance(cached, str):
            return json.loads(cached)
        return cached

    async def set(self, image_url: str, data: dict[str, Any], ttl: int | None = None) -> None:
        """Cache visual analysis ``data`` for ``image_url`` as JSON with TTL."""
        ex = ttl if ttl is not None else self._ttl
        await self._redis.set(self._key(image_url), json.dumps(data), ex=ex)
