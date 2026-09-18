import time

import fakeredis.aioredis
import pytest

from app.middleware.rate_limit import RateLimiter


@pytest.fixture
def redis():
    return fakeredis.aioredis.FakeRedis(decode_responses=True)


@pytest.mark.asyncio
async def test_allows_requests_under_limit(redis):
    limiter = RateLimiter(redis, limit=3, window=60)
    for _ in range(3):
        result = await limiter.check("client:1:/agents/query")
        assert result.allowed is True
    assert result.remaining == 0


@pytest.mark.asyncio
async def test_rejects_requests_over_limit(redis):
    limiter = RateLimiter(redis, limit=2, window=60)
    await limiter.check("client:1:/agents/query")
    await limiter.check("client:1:/agents/query")
    result = await limiter.check("client:1:/agents/query")
    assert result.allowed is False
    assert result.retry_after > 0


@pytest.mark.asyncio
async def test_window_expiry_allows_again(redis):
    limiter = RateLimiter(redis, limit=1, window=60)
    first = await limiter.check("client:1:/agents/query")
    assert first.allowed is True
    # Simulate the window elapsing by removing the scored entries.
    key = "ratelimit:client:1:/agents/query"
    await redis.zremrangebyscore(key, 0, time.time())
    second = await limiter.check("client:1:/agents/query")
    assert second.allowed is True


@pytest.mark.asyncio
async def test_keys_are_scoped_per_client_and_path(redis):
    limiter = RateLimiter(redis, limit=1, window=60)
    assert (await limiter.check("client:a:/agents/query")).allowed is True
    assert (await limiter.check("client:b:/agents/query")).allowed is True
    assert (await limiter.check("client:a:/agents/query/stream")).allowed is True
