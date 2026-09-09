"""Tests for the top-level concierge orchestrator (app/workflows/concierge_workflow.py).

The orchestrator runs the Intent Gate, delegates to the three agent sub-graphs
(placeholder passthroughs for now), and formulates a final response. Routing is
deterministic and testable without an LLM or a live database.
"""

import pytest

from app.core.config import get_settings
from app.customer_resolution import CustomerCandidate, CustomerResolution
from app.schemas.response import AgentStatus
from app.workflows.concierge_workflow import (
    build_concierge_graph,
    run_concierge,
    run_resolve_customer,
)


async def _invoke(message: str, org_context: dict | None = None) -> dict:
    graph = build_concierge_graph()
    initial = {
        "message": message,
        "org_context": org_context or {},
        "intent": None,
        "memory_output": None,
        "visual_output": None,
        "commerce_output": None,
        "response": None,
    }
    return await graph.ainvoke(initial)


async def test_item_search_routes_through_memory_and_visual():
    result = await _invoke("Do you have a blue saree for a wedding?")
    assert result["intent"]["intent_type"] == "item_search"
    assert result["memory_output"] is not None
    assert result["visual_output"] is not None
    assert result["commerce_output"] is None
    assert result["response"]["status"] == AgentStatus.success


async def test_pricing_query_routes_through_memory_and_commerce():
    result = await _invoke("How much is this dress?")
    assert result["intent"]["intent_type"] == "pricing_query"
    assert result["memory_output"] is not None
    assert result["visual_output"] is None
    assert result["commerce_output"] is not None


async def test_visual_stub_emits_structured_output():
    result = await _invoke("Do you have a blue saree for a wedding?")
    visual = result["visual_output"]

    assert visual is not None
    assert visual["ran"] is True
    # The stub declares its structured output shape so it is machine-checkable; the real
    # Slice 2 graph replaces "stub" with "success"/"pending" and fills items/looks.
    assert visual["status"] == "stub"
    assert "note" in visual
    assert visual["items"] == []
    assert visual["looks"] == []


async def test_commerce_stub_emits_structured_output():
    result = await _invoke("How much is this dress?")
    commerce = result["commerce_output"]

    assert commerce is not None
    assert commerce["ran"] is True
    assert commerce["status"] == "stub"
    assert "note" in commerce
    assert commerce["needs_approval"] is False


async def test_out_of_scope_short_circuits():
    result = await _invoke("Write me a python script to sort a list")
    assert result["intent"]["intent_type"] == "out_of_scope"
    assert result["intent"]["is_relevant"] is False
    # No agent should run for out-of-scope input.
    assert result["memory_output"] is None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None
    assert result["response"]["status"] == AgentStatus.out_of_scope


async def test_general_inquiry_runs_memory_only():
    result = await _invoke("Hello, how are you?")
    assert result["intent"]["intent_type"] == "general_inquiry"
    assert result["memory_output"] is not None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None


async def test_memory_agent_parses_message_without_customer_context():
    result = await _invoke("Do you have a blue saree for a wedding?")
    parsed = result["memory_output"]["parsed_intent"]
    assert parsed["intent_type"] == "item_search"
    assert parsed.get("occasion") == "wedding"
    assert parsed.get("color") == "blue"


@pytest.mark.asyncio
async def test_run_concierge_returns_response():
    response = await run_concierge("Do you have a blue saree?")
    assert response.status == AgentStatus.success
    assert response.output is not None


@pytest.mark.asyncio
async def test_run_concierge_out_of_scope():
    response = await run_concierge("Tell me a joke")
    assert response.status == AgentStatus.out_of_scope


@pytest.mark.asyncio
async def test_run_concierge_emits_lifecycle_states():
    """The on_state callback receives states as the workflow progresses."""
    states: list[str] = []

    async def on_state(state):
        states.append(state.value)

    response = await run_concierge(
        "Do you have a blue saree for a wedding?",
        on_state=on_state,
    )

    assert response.status == AgentStatus.success
    # intent_gate -> thinking, memory_agent -> searching, visual_agent -> searching,
    # formulate_response -> processing.
    assert "thinking" in states
    assert "searching" in states
    assert "processing" in states
    # The last emitted state is processing (formulate_response) before the terminal success.
    assert states[-1] == "processing"


@pytest.mark.asyncio
async def test_run_concierge_out_of_scope_emits_thinking_then_processing():
    """Out-of-scope short-circuits: intent gate (thinking) then formulate (processing)."""
    states: list[str] = []

    async def on_state(state):
        states.append(state.value)

    response = await run_concierge("Tell me a joke", on_state=on_state)

    assert response.status == AgentStatus.out_of_scope
    assert states == ["thinking", "processing"]


@pytest.mark.asyncio
async def test_run_concierge_preserves_state_order_with_delay(monkeypatch):
    """A configured state delay must not reorder or drop emitted lifecycle states."""
    monkeypatch.setattr(get_settings(), "agent_state_delay_ms", 1)
    states: list[str] = []

    async def on_state(state):
        states.append(state.value)

    response = await run_concierge(
        "Do you have a blue saree for a wedding?",
        on_state=on_state,
    )

    assert response.status == AgentStatus.success
    assert states == ["thinking", "searching", "searching", "processing"]


# ---------------------------------------------------------------------------
# Shared customer resolution (Issue #161)
# ---------------------------------------------------------------------------


def _make_resolver(*, kind, **kwargs):
    """Return an async resolver double producing a fixed CustomerResolution."""

    async def fake(org_id, message, *, registry, customer_id=None, phone=None):
        return CustomerResolution(kind=kind, message=message, **kwargs)

    return fake


@pytest.mark.asyncio
async def test_ambiguous_resolution_short_circuits_to_clarification(monkeypatch):
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _make_resolver(
            kind="ambiguous",
            candidates=[
                CustomerCandidate(customer_id="c1", full_name="Samantha Arias", status="vip"),
                CustomerCandidate(customer_id="c2", full_name="Samantha R", status="returning"),
            ],
        ),
    )

    result = await _invoke("Any events for Samantha Arias?", {"organization_id": "org-1"})

    assert result["resolution"]["kind"] == "ambiguous"
    # No specialist should run while we are still deciding which customer.
    assert result["memory_output"] is None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None
    assert result["response"]["output"]["clarification"]["kind"] == "ambiguous"


@pytest.mark.asyncio
async def test_not_found_resolution_asks_for_phone(monkeypatch):
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _make_resolver(kind="not_found"),
    )

    result = await _invoke("Any events for Zara Nobody?", {"organization_id": "org-1"})

    assert result["resolution"]["kind"] == "not_found"
    assert result["memory_output"] is None
    assert result["response"]["output"]["clarification"]["kind"] == "not_found"


@pytest.mark.asyncio
async def test_resolve_node_records_explicit_customer(monkeypatch):
    calls: list[tuple[str, str | None, str | None]] = []

    async def fake(org_id, message, *, registry, customer_id=None, phone=None):
        calls.append((org_id, customer_id, phone))
        return CustomerResolution(kind="resolved", customer_id="c1", profile={"fullName": "Samantha"})

    monkeypatch.setattr("app.workflows.concierge_workflow.resolve_customer", fake)

    out = await run_resolve_customer(
        {"message": "Any events for Samantha?", "org_context": {"organization_id": "org-1", "customer_id": "c1"}}
    )

    assert out["resolution"]["kind"] == "resolved"
    assert calls == [("org-1", "c1", None)]
