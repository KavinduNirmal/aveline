import os

import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient

from app.core.config import get_settings
from app.core.security import require_internal_token
from app.main import app

TEST_INTERNAL_TOKEN = "test-internal-token"


@pytest.fixture(autouse=True)
def _configure_internal_token():
    """Ensure a known internal token is configured for every test."""
    os.environ["INTERNAL_API_TOKEN"] = TEST_INTERNAL_TOKEN
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_health_is_public():
    response = TestClient(app).get("/health")
    assert response.status_code == 200


def test_ping_missing_token_returns_401():
    response = TestClient(app).post(
        "/agents/ping",
        json={"userId": "user_1", "roles": ["associate"]},
    )
    assert response.status_code == 401


def test_ping_invalid_token_returns_401():
    response = TestClient(app).post(
        "/agents/ping",
        headers={"X-Internal-Token": "wrong-token"},
        json={"userId": "user_1", "roles": ["associate"]},
    )
    assert response.status_code == 401


def test_ping_valid_token_returns_200_and_echoes_payload():
    response = TestClient(app).post(
        "/agents/ping",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json={"userId": "user_1", "roles": ["associate"]},
    )
    assert response.status_code == 200
    assert response.json()["echo"]["userId"] == "user_1"
    assert response.json()["echo"]["roles"] == ["associate"]


def test_ping_valid_token_logs_user_context(caplog):
    with caplog.at_level("INFO", logger="aveline.agent.api"):
        TestClient(app).post(
            "/agents/ping",
            headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
            json={"userId": "user_42", "roles": ["owner"]},
        )
    assert "user_id=user_42" in caplog.text
    assert "roles=['owner']" in caplog.text


@pytest.mark.asyncio
async def test_require_internal_token_accepts_valid_token():
    await require_internal_token(x_internal_token=TEST_INTERNAL_TOKEN)


@pytest.mark.asyncio
async def test_require_internal_token_rejects_missing_token():
    with pytest.raises(HTTPException) as exc:
        await require_internal_token(x_internal_token=None)
    assert exc.value.status_code == 401


@pytest.mark.asyncio
async def test_require_internal_token_rejects_invalid_token():
    with pytest.raises(HTTPException) as exc:
        await require_internal_token(x_internal_token="not-the-token")
    assert exc.value.status_code == 401


@pytest.mark.asyncio
async def test_require_internal_token_returns_500_when_unconfigured(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_TOKEN", "")
    get_settings.cache_clear()
    with pytest.raises(HTTPException) as exc:
        await require_internal_token(x_internal_token="anything")
    assert exc.value.status_code == 500
    get_settings.cache_clear()
