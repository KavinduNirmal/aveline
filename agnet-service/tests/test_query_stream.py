import os

import pytest
from fastapi.testclient import TestClient

from app.core.config import get_settings
from app.main import app

TEST_INTERNAL_TOKEN = "test-internal-token"
QUERY_PAYLOAD = {"query": "What is the margin on order 42?", "thread_id": "thread-1"}


@pytest.fixture(autouse=True)
def _configure_internal_token():
    os.environ["INTERNAL_API_TOKEN"] = TEST_INTERNAL_TOKEN
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_query_missing_token_returns_401():
    response = TestClient(app).post("/agents/query", json=QUERY_PAYLOAD)
    assert response.status_code == 401


def test_query_valid_token_returns_result():
    response = TestClient(app).post(
        "/agents/query",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json=QUERY_PAYLOAD,
    )
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert "result" in body


def test_query_stream_missing_token_returns_401():
    response = TestClient(app).post("/agents/query/stream", json=QUERY_PAYLOAD)
    assert response.status_code == 401


def test_query_stream_returns_sse_with_no_buffering_header():
    client = TestClient(app)
    with client.stream(
        "POST",
        "/agents/query/stream",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json=QUERY_PAYLOAD,
    ) as response:
        assert response.status_code == 200
        assert response.headers["X-Accel-Buffering"] == "no"
        assert response.headers["content-type"].startswith("text/event-stream")
        body = "".join(response.iter_text())
    assert "data:" in body
