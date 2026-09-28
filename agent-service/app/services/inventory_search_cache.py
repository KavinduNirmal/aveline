"""Inventory search cache with multi-tenant organization isolation and invalidation lifecycle.

Caches results of inventory searches in Redis to avoid repeated backend queries.
Keys are deterministically generated from criteria including org_id and search filters.
Provides lifecycle invalidation hooks for item creation, updates, stock changes,
status changes, and deletions.
"""

import hashlib
import json
import logging
from typing import Any

logger = logging.getLogger("aveline.agent.inventory_search_cache")

_PREFIX = "inventory_search:"


class InventorySearchCache:
    """Redis-backed inventory search cache ensuring tenant isolation by org_id and lifecycle invalidation."""

    def __init__(self, redis: Any, ttl: int = 300) -> None:
        self._redis = redis
        self._ttl = ttl

    @property
    def ttl(self) -> int:
        return self._ttl

    def _build_key(self, criteria: dict[str, Any]) -> str:
        """Return a deterministic key for ``criteria`` scoped by organization."""
        org_id = str(criteria.get("org_id", "global"))
        canonical = json.dumps(criteria, sort_keys=True, default=str)
        digest = hashlib.sha256(canonical.encode()).hexdigest()
        return f"{_PREFIX}{org_id}:{digest}"

    async def get(self, criteria: dict[str, Any]) -> list[Any] | None:
        """Return cached inventory search results for ``criteria``, or ``None`` on a miss."""
        try:
            raw = await self._redis.get(self._build_key(criteria))
            if raw is None:
                return None
            return json.loads(raw)
        except Exception:
            logger.warning("Failed to retrieve inventory search cache for criteria: %s", criteria, exc_info=True)
            return None

    async def set(self, criteria: dict[str, Any], results: list[Any], ttl: int | None = None) -> None:
        """Cache ``results`` for ``criteria`` with the configured or custom TTL."""
        try:
            effective_ttl = self._ttl if ttl is None else ttl
            await self._redis.set(
                self._build_key(criteria),
                json.dumps(results, default=str),
                ex=effective_ttl,
            )
        except Exception:
            logger.warning("Failed to store inventory search cache for criteria: %s", criteria, exc_info=True)

    async def get_inventory_search(self, criteria: dict[str, Any]) -> list[Any] | None:
        """Alias for get()."""
        return await self.get(criteria)

    async def set_inventory_search(self, criteria: dict[str, Any], results: list[Any], ttl: int | None = None) -> None:
        """Alias for set()."""
        await self.set(criteria, results, ttl=ttl)

    async def invalidate_for_org(self, org_id: str) -> int:
        """Delete all cached inventory search results for a specific organization."""
        try:
            pattern = f"{_PREFIX}{org_id}:*"
            keys: list[str] = []
            if hasattr(self._redis, "scan_iter"):
                async for key in self._redis.scan_iter(match=pattern):
                    keys.append(key)
            elif hasattr(self._redis, "keys"):
                res = await self._redis.keys(pattern)
                keys = list(res)
            if keys:
                await self._redis.delete(*keys)
                logger.info("Invalidated %d inventory cache entries for org '%s'.", len(keys), org_id)
                return len(keys)
            return 0
        except Exception:
            logger.warning("Failed to invalidate cache for org '%s'", org_id, exc_info=True)
            return 0

    async def invalidate(self, org_id: str | None = None) -> int:
        """Invalidate cache entries for a specific org or all orgs."""
        if org_id is not None:
            return await self.invalidate_for_org(org_id)
        try:
            pattern = f"{_PREFIX}*"
            keys: list[str] = []
            if hasattr(self._redis, "scan_iter"):
                async for key in self._redis.scan_iter(match=pattern):
                    keys.append(key)
            elif hasattr(self._redis, "keys"):
                res = await self._redis.keys(pattern)
                keys = list(res)
            if keys:
                await self._redis.delete(*keys)
                return len(keys)
            return 0
        except Exception:
            logger.warning("Failed to invalidate all inventory search cache", exc_info=True)
            return 0

    # -----------------------------------------------------------------------
    # Invalidation Lifecycle Hooks
    # -----------------------------------------------------------------------

    async def on_inventory_item_created(self, org_id: str, item_id: str | None = None, **kwargs: Any) -> int:
        """Invalidate search cache when an inventory item is created."""
        return await self.invalidate_for_org(org_id)

    async def on_inventory_item_updated(self, org_id: str, item_id: str | None = None, **kwargs: Any) -> int:
        """Invalidate search cache when an inventory item is updated."""
        return await self.invalidate_for_org(org_id)

    async def on_stock_quantity_changed(
        self, org_id: str, item_id: str | None = None, new_quantity: int | None = None, **kwargs: Any
    ) -> int:
        """Invalidate search cache when stock quantity changes."""
        return await self.invalidate_for_org(org_id)

    async def on_item_status_changed(
        self, org_id: str, item_id: str | None = None, new_status: str | None = None, **kwargs: Any
    ) -> int:
        """Invalidate search cache when item status changes."""
        return await self.invalidate_for_org(org_id)

    async def on_inventory_item_deleted(self, org_id: str, item_id: str | None = None, **kwargs: Any) -> int:
        """Invalidate search cache when an item is soft or hard deleted."""
        return await self.invalidate_for_org(org_id)

    # Aliases
    handle_item_created = on_inventory_item_created
    handle_item_updated = on_inventory_item_updated
    handle_stock_changed = on_stock_quantity_changed
    handle_status_changed = on_item_status_changed
    handle_item_deleted = on_inventory_item_deleted
