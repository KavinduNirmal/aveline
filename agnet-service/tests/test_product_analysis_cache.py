"""Unit tests for ProductAnalysisCache (TDD)."""

from unittest.mock import AsyncMock
import pytest

from app.services.product_analysis_cache import ProductAnalysisCache


@pytest.mark.asyncio
async def test_product_analysis_cache_returns_cached_result():
    redis = AsyncMock()
    redis.get.return_value = '{"color":"emerald","fabric":"silk"}'

    cache = ProductAnalysisCache(redis)

    result = await cache.get("https://example.com/item.jpg")

    assert result["color"] == "emerald"


@pytest.mark.asyncio
async def test_product_analysis_cache_returns_none_on_miss():
    redis = AsyncMock()
    redis.get.return_value = None

    cache = ProductAnalysisCache(redis)

    result = await cache.get("image")

    assert result is None


@pytest.mark.asyncio
async def test_product_analysis_cache_uses_one_hour_ttl():
    redis = AsyncMock()
    cache = ProductAnalysisCache(redis)

    assert cache.ttl == 3600

    await cache.set(
        "https://example.com/item.jpg",
        {"color": "emerald", "fabric": "silk"},
    )

    redis.set.assert_awaited_once()
    _, kwargs = redis.set.call_args
    assert kwargs.get("ex") == 3600
