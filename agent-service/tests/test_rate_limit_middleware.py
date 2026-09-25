import fakeredis.aioredis
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.middleware.rate_limit import RateLimitMiddleware


@pytest.fixture
def redis():
    return fakeredis.aioredis.FakeRedis(decode_responses=True)


def _make_app(redis, limit=2, window=60):
    app = FastAPI()
    app.add_middleware(RateLimitMiddleware, redis=redis, limit=limit, window=window)

    @app.post("/agents/query")
    async def query():
        return {"ok": True}

    @app.post("/agents/query/stream")
    async def query_stream():
        return {"ok": True}

    @app.get("/health")
    async def health():
        return {"status": "ok"}

    return app


def test_rate_limit_applies_to_agents_query(redis):
    app = _make_app(redis, limit=2)
    client = TestClient(app)
    for _ in range(2):
        response = client.post("/agents/query")
        assert response.status_code == 200
        assert response.headers["X-RateLimit-Limit"] == "2"
    response = client.post("/agents/query")
    assert response.status_code == 429
    assert response.headers["X-RateLimit-Remaining"] == "0"
    assert int(response.headers["X-RateLimit-Reset"]) > 0


def test_rate_limit_applies_to_agents_query_stream(redis):
    app = _make_app(redis, limit=1)
    client = TestClient(app)
    assert client.post("/agents/query/stream").status_code == 200
    assert client.post("/agents/query/stream").status_code == 429


def test_rate_limit_does_not_apply_to_health(redis):
    app = _make_app(redis, limit=1)
    client = TestClient(app)
    # Exhaust the limit on /agents/query, then confirm /health is unaffected.
    client.post("/agents/query")
    assert client.post("/agents/query").status_code == 429
    assert client.get("/health").status_code == 200
