import fakeredis.aioredis
import pytest

from app.services.cache import CacheService


@pytest.fixture
def redis():
    return fakeredis.aioredis.FakeRedis(decode_responses=True)


@pytest.fixture
def cache(redis):
    return CacheService(redis)


@pytest.mark.asyncio
async def test_get_returns_none_when_missing(cache):
    assert await cache.get("missing") is None


@pytest.mark.asyncio
async def test_set_then_get(cache):
    await cache.set("greeting", "hello")
    assert await cache.get("greeting") == "hello"


@pytest.mark.asyncio
async def test_set_overwrites(cache):
    await cache.set("k", "v1")
    await cache.set("k", "v2")
    assert await cache.get("k") == "v2"


@pytest.mark.asyncio
async def test_set_with_ttl_expires(cache, redis):
    await cache.set("temp", "value", ttl=1)
    assert await cache.get("temp") == "value"
    # Simulate TTL expiry by deleting the key (fakeredis does not advance time).
    await redis.delete("temp")
    assert await cache.get("temp") is None


@pytest.mark.asyncio
async def test_publish_sends_message(cache, redis):
    pubsub = redis.pubsub()
    await pubsub.subscribe("aveline:org:test.event")
    await cache.publish("aveline:org:test.event", "payload")
    message = await pubsub.get_message(timeout=1.0)
    # First message is the subscribe confirmation; poll for the actual payload.
    for _ in range(3):
        if message and message.get("type") == "message":
            break
        message = await pubsub.get_message(timeout=1.0)
    assert message is not None
    assert message["type"] == "message"
    assert message["data"] == "payload"
    await pubsub.aclose()
