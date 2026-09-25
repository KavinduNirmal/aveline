"""Consent enforcement at orchestrator scope (privacy/consent plan §11 Phase 1).

The API's ingress gate (item 1.2) stops a revoked customer's message before dispatch, but the
agent service must not depend on the caller being correct: the guard has to hold inside the
workflow too. These tests pin the three agent-side obligations:

* **1.3** a revoked customer never reaches ``visual_agent`` or ``commerce_agent``;
* **1.5** ``POST /agents/query/stream`` is guarded as well, even though it bypasses
  ``run_concierge``;
* **1.6** a consent skip reports a distinct status, not ``success``/``Succeeded``.
"""

import os
from contextlib import asynccontextmanager

import pytest
from fastapi.testclient import TestClient
from langgraph.checkpoint.memory import InMemorySaver

from app.api.agents import _run_status_from_response
from app.core.config import get_settings
from app.main import app
from app.schemas.response import AgentResponse, AgentStatus
from app.workflows.concierge_workflow import (
    build_concierge_graph,
    run_concierge,
    run_memory_agent,
)

# A local-only placeholder: it is both the token the test sets and the one it sends.
TEST_INTERNAL_TOKEN = "local-development-placeholder-token"

#: What the API sends for an inbound WhatsApp message with a bound customer.
_INBOUND_CONTEXT = {
    "organization_id": "org-1",
    "customer_id": "cust-1",
    "phone_number": "+94771234567",
    "channel": "whatsapp",
    "direction": "inbound",
}

#: A message the intent gate routes through the memory agent and on to the visual agent, so the
#: downstream run is real rather than an artefact of routing.
_STYLING_MESSAGE = "Do you have a blue saree for a wedding?"


async def _invoke(message: str, org_context: dict) -> dict:
    graph = build_concierge_graph()
    return await graph.ainvoke(
        {
            "message": message,
            "org_context": org_context,
            "history": [],
            "thread_summary": None,
            "pinned_slots": {},
            "intent": None,
            "resolution": None,
            "memory_output": None,
            "visual_output": None,
            "commerce_output": None,
            "usage": None,
            "response": None,
        }
    )


@pytest.fixture
def _recording_specialists(monkeypatch):
    """Patch the three specialists so "did it run?" is a recorded call, not an inference.

    ``run_memory_agent`` returns a revoked consent status; visual and commerce count their calls.
    """
    calls = {"visual": 0, "commerce": 0}

    async def memory(state):
        return {
            "memory_output": {
                "agent": "memory",
                "ran": True,
                "status": "skipped",
                "reason": "customer has revoked consent",
            },
            "consent_status": "revoked",
        }

    async def visual(state):
        calls["visual"] += 1
        return {"visual_output": {"agent": "visual", "ran": True, "status": "success"}}

    async def commerce(state):
        calls["commerce"] += 1
        return {"commerce_output": {"agent": "commerce", "ran": True, "status": "success"}}

    monkeypatch.setattr("app.workflows.concierge_workflow.run_memory_agent", memory)
    monkeypatch.setattr("app.workflows.concierge_workflow.run_visual_agent", visual)
    monkeypatch.setattr("app.workflows.concierge_workflow.run_commerce_agent", commerce)
    return calls


# ---------------------------------------------------------------------------
# 1.3 - orchestrator-scope guard
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_revoked_customer_never_reaches_visual_or_commerce(_recording_specialists):
    """The memory agent's own skip used to close only its sub-graph: `_route_after_memory` read
    the intent, so `run_visual_agent` and `run_commerce_agent` still ran for a revoked customer.
    """
    result = await _invoke(_STYLING_MESSAGE, _INBOUND_CONTEXT)

    assert _recording_specialists == {"visual": 0, "commerce": 0}, (
        "a revoked customer's message reached a downstream specialist"
    )
    assert result["visual_output"] is None
    assert result["commerce_output"] is None


@pytest.mark.asyncio
async def test_revoked_customer_reports_the_skip_status(_recording_specialists):
    # 1.6: the terminal status must say "skipped", not "success".
    result = await _invoke(_STYLING_MESSAGE, _INBOUND_CONTEXT)

    assert result["response"]["status"] == AgentStatus.skipped
    assert result["consent_status"] == "revoked"


@pytest.mark.asyncio
async def test_consent_check_unavailable_also_stops_the_run(monkeypatch):
    """Fail closed means the whole ordered pipeline stops, not just the memory sub-graph."""

    async def memory(state):
        return {
            "memory_output": {"agent": "memory", "ran": True, "status": "skipped", "reason": "consent check unavailable"},
            "consent_status": "unavailable",
        }

    calls = {"visual": 0, "commerce": 0}

    async def visual(state):
        calls["visual"] += 1
        return {"visual_output": {"agent": "visual", "ran": True, "status": "success"}}

    async def commerce(state):
        calls["commerce"] += 1
        return {"commerce_output": {"agent": "commerce", "ran": True, "status": "success"}}

    monkeypatch.setattr("app.workflows.concierge_workflow.run_memory_agent", memory)
    monkeypatch.setattr("app.workflows.concierge_workflow.run_visual_agent", visual)
    monkeypatch.setattr("app.workflows.concierge_workflow.run_commerce_agent", commerce)

    result = await _invoke(_STYLING_MESSAGE, _INBOUND_CONTEXT)

    assert calls == {"visual": 0, "commerce": 0}
    assert result["response"]["status"] == AgentStatus.skipped


@pytest.mark.asyncio
async def test_run_memory_agent_surfaces_the_sub_graph_consent_status(monkeypatch):
    """1.3: the status has to leave the sub-graph and reach the orchestrator's state."""

    class FakeMemoryGraph:
        async def ainvoke(self, state):
            return {
                "status": "skipped",
                "reason": "customer has revoked consent",
                "consent_status": "revoked",
                "output": {
                    "status": "skipped",
                    "reason": "customer has revoked consent",
                    "extracted_memories": [],
                    "detected_events": [],
                },
            }

    monkeypatch.setattr(
        "app.workflows.concierge_workflow.build_memory_graph",
        lambda *args, **kwargs: FakeMemoryGraph(),
    )

    result = await run_memory_agent(
        {
            "message": _STYLING_MESSAGE,
            "org_context": _INBOUND_CONTEXT,
            "intent": {"intent_type": "item_search"},
        }
    )

    assert result["consent_status"] == "revoked"
    assert result["memory_output"]["status"] == "skipped"


# ---------------------------------------------------------------------------
# 1.6 - run-status reporting
# ---------------------------------------------------------------------------


def test_a_skip_maps_to_the_backend_skipped_run_status():
    response = AgentResponse(status=AgentStatus.skipped, output={}, metadata=None)

    assert _run_status_from_response(response) == "Skipped"


@pytest.mark.asyncio
async def test_run_concierge_reports_a_consent_skip_as_skipped(monkeypatch):
    async def memory(state):
        return {
            "memory_output": {"agent": "memory", "ran": True, "status": "skipped", "reason": "customer has revoked consent"},
            "consent_status": "revoked",
        }

    monkeypatch.setattr("app.workflows.concierge_workflow.run_memory_agent", memory)

    response = await run_concierge(_STYLING_MESSAGE, org_context=_INBOUND_CONTEXT)

    assert response.status == AgentStatus.skipped
    assert response.status != AgentStatus.success


def test_query_endpoint_reports_a_consent_skip_as_skipped(monkeypatch):
    """The acceptance criterion is stated at the wire: `/agents/query` must not answer `success`."""

    class RevokedRegistry:
        async def get_customer_consent(self, org_id, customer_id):
            return {"consentStatus": "revoked"}

    monkeypatch.setattr(
        "app.workflows.concierge_workflow.ToolRegistry", lambda *args, **kwargs: RevokedRegistry()
    )

    client = TestClient(app)
    response = client.post(
        "/agents/query",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json={
            "query": _STYLING_MESSAGE,
            "thread_id": "thread-status",
            "org_context": _INBOUND_CONTEXT,
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["result"]["status"] == "skipped"
    assert body["result"]["status"] != "success"


# ---------------------------------------------------------------------------
# 1.5 - the streaming route (it bypasses run_concierge)
# ---------------------------------------------------------------------------


@pytest.fixture(autouse=True)
def _configure_internal_token():
    os.environ["INTERNAL_API_TOKEN"] = TEST_INTERNAL_TOKEN
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


@pytest.fixture(autouse=True)
def _noop_checkpointer(monkeypatch):
    @asynccontextmanager
    async def fake_checkpointer(*args, **kwargs):
        yield InMemorySaver()

    monkeypatch.setattr("app.workflows.concierge_workflow.create_checkpointer", fake_checkpointer)


def _stream(payload: dict) -> str:
    client = TestClient(app)
    with client.stream(
        "POST",
        "/agents/query/stream",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json=payload,
    ) as response:
        assert response.status_code == 200
        return "".join(response.iter_text())


def _install_stream_registry(monkeypatch, *, consent: dict | None, raises: bool = False):
    class StubRegistry:
        async def get_customer_consent(self, org_id, customer_id):
            if raises:
                raise RuntimeError("consent store unavailable")
            return consent

    monkeypatch.setattr("app.api.agents.ToolRegistry", lambda *args, **kwargs: StubRegistry())


@pytest.fixture
def _stream_specialist_counters(monkeypatch):
    calls = {"visual": 0, "commerce": 0}

    async def visual(state):
        calls["visual"] += 1
        return {"visual_output": {"agent": "visual", "ran": True, "status": "success"}}

    async def commerce(state):
        calls["commerce"] += 1
        return {"commerce_output": {"agent": "commerce", "ran": True, "status": "success"}}

    monkeypatch.setattr("app.workflows.concierge_workflow.run_visual_agent", visual)
    monkeypatch.setattr("app.workflows.concierge_workflow.run_commerce_agent", commerce)
    return calls


def test_query_stream_revoked_customer_is_guarded(monkeypatch, _stream_specialist_counters):
    _install_stream_registry(monkeypatch, consent={"consentStatus": "revoked"})

    body = _stream(
        {"query": _STYLING_MESSAGE, "thread_id": "thread-revoked", "org_context": _INBOUND_CONTEXT}
    )

    assert "consent" in body
    assert _stream_specialist_counters == {"visual": 0, "commerce": 0}


def test_query_stream_consent_failure_skips_instead_of_raising(monkeypatch, _stream_specialist_counters):
    _install_stream_registry(monkeypatch, consent=None, raises=True)

    body = _stream(
        {"query": _STYLING_MESSAGE, "thread_id": "thread-fail", "org_context": _INBOUND_CONTEXT}
    )

    assert "consent_check_unavailable" in body
    assert _stream_specialist_counters == {"visual": 0, "commerce": 0}


def test_query_stream_consenting_customer_passes_the_route_guard(monkeypatch, _stream_specialist_counters):
    """The positive control: without it the two tests above pass vacuously."""
    _install_stream_registry(monkeypatch, consent={"consentStatus": "granted"})

    body = _stream(
        {"query": _STYLING_MESSAGE, "thread_id": "thread-ok", "org_context": _INBOUND_CONTEXT}
    )

    assert "consent_skipped" not in body
