import os

import pytest
from fastapi.testclient import TestClient

from app.core.config import get_settings
from app.main import app


@pytest.fixture(autouse=True)
def _configure_internal_token():
    os.environ["INTERNAL_API_TOKEN"] = "test-internal-token"
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_health_ready_returns_200_when_db_reachable(monkeypatch):
    async def fake_check():
        return True

    monkeypatch.setattr("app.api.health.check_db_connection", fake_check)
    response = TestClient(app).get("/health/ready")
    assert response.status_code == 200
    assert response.json()["status"] == "ready"


def test_health_ready_returns_503_when_db_unreachable(monkeypatch):
    async def fake_check():
        raise RuntimeError("db down")

    monkeypatch.setattr("app.api.health.check_db_connection", fake_check)
    response = TestClient(app).get("/health/ready")
    assert response.status_code == 503
    assert response.json()["status"] == "not_ready"
