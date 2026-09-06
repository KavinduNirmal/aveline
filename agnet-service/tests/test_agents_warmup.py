import os

import pytest
from fastapi.testclient import TestClient

from app.core.config import get_settings
from app.main import app

TEST_INTERNAL_TOKEN = "test-internal-token"
WARMUP_PAYLOAD = {
    "organizationId": "9a80795c-68a6-4c2b-9d8f-3d0c7e5f1b22",
    "boutiqueName": "The Silk Pavilion",
    "planTier": "Orchid",
    "brandVoice": "Poised and discreet",
    "businessRules": "Max discount 15%, 14-day exchange",
    "preferredColorsFabrics": "Raw silk, Pashmina, Hand-woven linen",
    "customerPreferences": "Greet with Ceylon tea, note sizing preferences",
}


@pytest.fixture(autouse=True)
def _configure_internal_token():
    """Ensure a known internal token is configured for every test."""
    os.environ["INTERNAL_API_TOKEN"] = TEST_INTERNAL_TOKEN
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_warmup_missing_token_returns_401():
    response = TestClient(app).post("/agents/warmup", json=WARMUP_PAYLOAD)
    assert response.status_code == 401


def test_warmup_invalid_token_returns_401():
    response = TestClient(app).post(
        "/agents/warmup",
        headers={"X-Internal-Token": "wrong-token"},
        json=WARMUP_PAYLOAD,
    )
    assert response.status_code == 401


def test_warmup_valid_token_returns_warmed_status():
    response = TestClient(app).post(
        "/agents/warmup",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json=WARMUP_PAYLOAD,
    )
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "warmed"
    assert body["ready"] is True
    assert body["organizationId"] == WARMUP_PAYLOAD["organizationId"]
    assert "CustomerMemoryAgent" in body["agents"]


def test_warmup_valid_token_logs_structured_event(caplog):
    with caplog.at_level("INFO", logger="aveline.agent.api"):
        TestClient(app).post(
            "/agents/warmup",
            headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
            json=WARMUP_PAYLOAD,
        )
    assert "org_id=9a80795c" in caplog.text
    assert caplog.records
    record = caplog.records[0]
    assert record.action == "agent_warmup"
    assert record.organization_id == WARMUP_PAYLOAD["organizationId"]
