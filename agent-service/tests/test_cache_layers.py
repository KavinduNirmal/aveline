"""Tests for the Redis-backed caching layers (semantic / profile / tool).

Each layer wraps the shared CacheService and adds a purpose-built key scheme.
All tests use fakeredis so no real Redis is required.
"""

import fakeredis.aioredis
import pytest

from app.services.profile_cache import ProfileCache
from app.services.semantic_cache import SemanticCache
from app.services.tool_cache import ToolCache


@pytest.fixture
def redis():
    return fakeredis.aioredis.FakeRedis(decode_responses=True)


# ---------------------------------------------------------------------------
# SemanticCache
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_semantic_cache_miss_returns_none(redis):
    cache = SemanticCache(redis)
    assert await cache.get("hello", "sys", "model-x") is None


@pytest.mark.asyncio
async def test_semantic_cache_set_then_get(redis):
    cache = SemanticCache(redis)
    await cache.set("hello", "sys", "model-x", "response-body")
    assert await cache.get("hello", "sys", "model-x") == "response-body"


@pytest.mark.asyncio
async def test_semantic_cache_key_is_deterministic(redis):
    cache = SemanticCache(redis)
    k1 = cache._build_key("hello", "sys", "model-x")
    k2 = cache._build_key("hello", "sys", "model-x")
    assert k1 == k2
    assert k1.startswith("semantic_cache:")


@pytest.mark.asyncio
async def test_semantic_cache_key_differs_by_model(redis):
    cache = SemanticCache(redis)
    assert cache._build_key("hello", "sys", "a") != cache._build_key("hello", "sys", "b")


@pytest.mark.asyncio
async def test_semantic_cache_invalidate(redis):
    cache = SemanticCache(redis)
    await cache.set("hello", "sys", "model-x", "resp")
    await cache.invalidate()
    assert await cache.get("hello", "sys", "model-x") is None


# ---------------------------------------------------------------------------
# ProfileCache
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_profile_cache_miss_returns_none(redis):
    cache = ProfileCache(redis)
    assert await cache.get_customer_profile("cust-1") is None


@pytest.mark.asyncio
async def test_profile_cache_round_trip(redis):
    cache = ProfileCache(redis)
    profile = {"name": "Sarah", "preferences": ["pastel", "silk"]}
    await cache.set_customer_profile("cust-1", profile)
    assert await cache.get_customer_profile("cust-1") == profile


@pytest.mark.asyncio
async def test_profile_cache_overwrites(redis):
    cache = ProfileCache(redis)
    await cache.set_customer_profile("cust-1", {"name": "A"})
    await cache.set_customer_profile("cust-1", {"name": "B"})
    assert await cache.get_customer_profile("cust-1") == {"name": "B"}


# ---------------------------------------------------------------------------
# ToolCache
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_tool_cache_miss_returns_none(redis):
    cache = ToolCache(redis)
    assert await cache.get_inventory_search({"color": "blue"}) is None


@pytest.mark.asyncio
async def test_tool_cache_round_trip(redis):
    cache = ToolCache(redis)
    criteria = {"color": "blue", "size": "M"}
    results = [{"id": 1, "name": "Saree"}]
    await cache.set_inventory_search(criteria, results)
    assert await cache.get_inventory_search(criteria) == results


@pytest.mark.asyncio
async def test_tool_cache_key_is_order_independent(redis):
    cache = ToolCache(redis)
    k1 = cache._build_key({"color": "blue", "size": "M"})
    k2 = cache._build_key({"size": "M", "color": "blue"})
    assert k1 == k2
