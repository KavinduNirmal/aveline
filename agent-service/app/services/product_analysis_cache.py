"""Product Analysis Cache for visual feature extraction results.

Caches visual attributes extracted from product images in Redis with a 1-hour
TTL (3600 seconds) to avoid redundant multimodal vision API calls.

The key is ``{orgId}:{publicId}`` and never the image URL: the API hands the agent an
absolute, single-use, rotating media token, so a URL key would be a different string on every
mint and the cache would never hit (strategy §5.1 S5, risk R14; Elle plan §7.4 item 4).

**This module is not wired into the analysis path.** Re-keying is deliberately paired with
*wiring* in the plan; wiring it here would put a Redis round trip in front of every analysis
before the strategy's §3.8 measurement asks for one. The key is corrected so that a future
wiring cannot silently guarantee 0% hits.
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

    def _key(self, org_id: str, public_id: str) -> str:
        """Return the tenant-scoped, URL-independent cache key."""
        return f"{_PREFIX}{org_id}:{public_id}"

    async def get(self, org_id: str, public_id: str) -> dict[str, Any] | None:
        """Return cached analysis attributes for ``{org_id}:{public_id}`` or ``None`` on miss."""
        cached = await self._redis.get(self._key(org_id, public_id))
        if cached is None:
            return None
        if isinstance(cached, bytes):
            cached = cached.decode("utf-8")
        if isinstance(cached, str):
            return json.loads(cached)
        return cached

    async def set(
        self,
        org_id: str,
        public_id: str,
        data: dict[str, Any],
        ttl: int | None = None,
    ) -> None:
        """Cache visual analysis ``data`` for ``{org_id}:{public_id}`` as JSON with TTL."""
        ex = ttl if ttl is not None else self._ttl
        await self._redis.set(self._key(org_id, public_id), json.dumps(data), ex=ex)
