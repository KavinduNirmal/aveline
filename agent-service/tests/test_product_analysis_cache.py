"""Unit tests for ProductAnalysisCache (TDD).

The cache is keyed by ``{orgId}:{publicId}``, never by the raw image URL: the tokenised
bridge URL rotates on every mint, so a URL key would guarantee 0% hits (risk R14,
strategy §4 C15 / Elle plan §7.4 item 4).
"""

from unittest.mock import AsyncMock

import pytest

from app.services.product_analysis_cache import ProductAnalysisCache

ORG_ID = "11111111-2222-3333-4444-555555555555"
PUBLIC_ID = "aveline/11111111-2222-3333-4444-555555555555/conversations/abc"
ROTATING_URL = "https://media.example/api/v1/media/one-shot-rotating-token"


@pytest.mark.asyncio
async def test_product_analysis_cache_returns_cached_result():
    redis = AsyncMock()
    redis.get.return_value = '{"color":"emerald","fabric":"silk"}'

    cache = ProductAnalysisCache(redis)

    result = await cache.get(ORG_ID, PUBLIC_ID)

    assert result["color"] == "emerald"


@pytest.mark.asyncio
async def test_product_analysis_cache_returns_none_on_miss():
    redis = AsyncMock()
    redis.get.return_value = None

    cache = ProductAnalysisCache(redis)

    result = await cache.get(ORG_ID, PUBLIC_ID)

    assert result is None


@pytest.mark.asyncio
async def test_cache_key_is_org_and_public_id_and_never_the_url():
    redis = AsyncMock()
    redis.get.return_value = None

    cache = ProductAnalysisCache(redis)
    await cache.get(ORG_ID, PUBLIC_ID)

    key = redis.get.call_args.args[0]
    assert key == f"product_analysis:{ORG_ID}:{PUBLIC_ID}"
    assert ROTATING_URL not in key


@pytest.mark.asyncio
async def test_product_analysis_cache_uses_one_hour_ttl():
    redis = AsyncMock()
    cache = ProductAnalysisCache(redis)

    assert cache.ttl == 3600

    await cache.set(ORG_ID, PUBLIC_ID, {"color": "emerald", "fabric": "silk"})

    redis.set.assert_awaited_once()
    key, _ = redis.set.call_args.args
    _, kwargs = redis.set.call_args
    assert key == f"product_analysis:{ORG_ID}:{PUBLIC_ID}"
    assert ROTATING_URL not in key
    assert kwargs.get("ex") == 3600
