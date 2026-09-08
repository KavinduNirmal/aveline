import os
from contextlib import asynccontextmanager

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


@pytest.fixture(autouse=True)
def _noop_checkpointer(monkeypatch):
    """Avoid a real Postgres connection when a thread_id triggers checkpointing."""

    @asynccontextmanager
    async def fake_checkpointer(*args, **kwargs):
        yield object()

    monkeypatch.setattr(
        "app.workflows.concierge_workflow.create_checkpointer", fake_checkpointer
    )


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
    assert body["result"]["status"] == "success"
    assert "output" in body["result"]


def test_query_out_of_scope_returns_out_of_scope_status():
    response = TestClient(app).post(
        "/agents/query",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json={"query": "Write me a python script", "thread_id": "thread-1"},
    )
    assert response.status_code == 200
    body = response.json()
    assert body["result"]["status"] == "out_of_scope"


def test_query_accepts_org_context():
    response = TestClient(app).post(
        "/agents/query",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json={
            "query": "Do you have a blue saree?",
            "thread_id": "thread-1",
            "org_context": {"plan_tier": "orchid", "brand_voice": "Elegant"},
        },
    )
    assert response.status_code == 200
    assert response.json()["result"]["status"] == "success"


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
