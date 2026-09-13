"""Unit tests for InventorySearchCache verifying multi-tenant organization isolation, caching, and invalidation lifecycle."""

from unittest.mock import AsyncMock
import fakeredis.aioredis
import pytest

from app.services.inventory_search_cache import InventorySearchCache


@pytest.fixture
def fake_redis():
    return fakeredis.aioredis.FakeRedis(decode_responses=True)


def test_inventory_cache_key_changes_between_organizations():
    cache = InventorySearchCache(AsyncMock())

    key1 = cache._build_key({
        "org_id": "org-1",
        "color": "emerald"
    })

    key2 = cache._build_key({
        "org_id": "org-2",
        "color": "emerald"
    })

    assert key1 != key2


def test_inventory_search_cache_key_is_order_independent():
    cache = InventorySearchCache(AsyncMock())

    key1 = cache._build_key({"org_id": "org-1", "color": "red", "size": "M"})
    key2 = cache._build_key({"size": "M", "color": "red", "org_id": "org-1"})

    assert key1 == key2


@pytest.mark.asyncio
async def test_inventory_search_cache_returns_cached_results():
    redis = AsyncMock()
    redis.get.return_value = '[{"item_id": "item-1", "item_name": "Emerald Saree", "price": 45000}]'

    cache = InventorySearchCache(redis)
    result = await cache.get({"org_id": "org-1", "color": "emerald"})

    assert result is not None
    assert len(result) == 1
    assert result[0]["item_name"] == "Emerald Saree"


@pytest.mark.asyncio
async def test_inventory_search_cache_returns_none_on_miss():
    redis = AsyncMock()
    redis.get.return_value = None

    cache = InventorySearchCache(redis)
    result = await cache.get({"org_id": "org-1", "color": "emerald"})

    assert result is None


@pytest.mark.asyncio
async def test_inventory_search_cache_set_uses_ttl():
    redis = AsyncMock()

    cache = InventorySearchCache(redis, ttl=300)
    criteria = {"org_id": "org-1", "category": "blouse"}
    items = [{"item_id": "item-2", "price": 12000}]

    await cache.set(criteria, items)

    key = cache._build_key(criteria)
    redis.set.assert_awaited_once()
    args, kwargs = redis.set.call_args
    assert args[0] == key
    assert kwargs.get("ex") == 300


# ---------------------------------------------------------------------------
# Invalidation Lifecycle Tests
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_inventory_search_cache_invalidated_on_item_created(fake_redis):
    cache = InventorySearchCache(fake_redis)
    criteria = {"org_id": "org-1", "color": "emerald"}
    await cache.set(criteria, [{"item_id": "item-1", "name": "Emerald Saree"}])

    assert await cache.get(criteria) is not None

    await cache.on_inventory_item_created(org_id="org-1", item_id="item-new")

    assert await cache.get(criteria) is None


@pytest.mark.asyncio
async def test_inventory_search_cache_invalidated_on_item_updated(fake_redis):
    cache = InventorySearchCache(fake_redis)
    criteria = {"org_id": "org-1", "color": "emerald"}
    await cache.set(criteria, [{"item_id": "item-1", "name": "Emerald Saree"}])

    assert await cache.get(criteria) is not None

    await cache.on_inventory_item_updated(org_id="org-1", item_id="item-1")

    assert await cache.get(criteria) is None


@pytest.mark.asyncio
async def test_inventory_search_cache_invalidated_on_stock_quantity_changed(fake_redis):
    cache = InventorySearchCache(fake_redis)
    criteria = {"org_id": "org-1", "color": "emerald"}
    await cache.set(criteria, [{"item_id": "item-1", "name": "Emerald Saree"}])

    assert await cache.get(criteria) is not None

    await cache.on_stock_quantity_changed(org_id="org-1", item_id="item-1", new_quantity=0)

    assert await cache.get(criteria) is None


@pytest.mark.asyncio
async def test_inventory_search_cache_invalidated_on_item_status_changed(fake_redis):
    cache = InventorySearchCache(fake_redis)
    criteria = {"org_id": "org-1", "color": "emerald"}
    await cache.set(criteria, [{"item_id": "item-1", "name": "Emerald Saree"}])

    assert await cache.get(criteria) is not None

    await cache.on_item_status_changed(org_id="org-1", item_id="item-1", new_status="sold")

    assert await cache.get(criteria) is None


@pytest.mark.asyncio
async def test_inventory_search_cache_invalidated_on_item_deleted(fake_redis):
    cache = InventorySearchCache(fake_redis)
    criteria = {"org_id": "org-1", "color": "emerald"}
    await cache.set(criteria, [{"item_id": "item-1", "name": "Emerald Saree"}])

    assert await cache.get(criteria) is not None

    await cache.on_inventory_item_deleted(org_id="org-1", item_id="item-1")

    assert await cache.get(criteria) is None


@pytest.mark.asyncio
async def test_invalidation_preserves_other_organizations_cache(fake_redis):
    cache = InventorySearchCache(fake_redis)
    criteria_org1 = {"org_id": "org-1", "color": "emerald"}
    criteria_org2 = {"org_id": "org-2", "color": "emerald"}

    await cache.set(criteria_org1, [{"item_id": "item-1", "name": "Org 1 Saree"}])
    await cache.set(criteria_org2, [{"item_id": "item-2", "name": "Org 2 Saree"}])

    # Invalidate org-1 only
    await cache.invalidate_for_org("org-1")

    # org-1 should be invalidated (miss)
    assert await cache.get(criteria_org1) is None

    # org-2 should remain warm and untouched (hit)
    org2_cached = await cache.get(criteria_org2)
    assert org2_cached is not None
    assert len(org2_cached) == 1
    assert org2_cached[0]["name"] == "Org 2 Saree"
