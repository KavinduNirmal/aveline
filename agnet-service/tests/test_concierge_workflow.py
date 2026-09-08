"""Tests for the top-level concierge orchestrator (app/workflows/concierge_workflow.py).

The orchestrator runs the Intent Gate, delegates to the three agent sub-graphs
(placeholder passthroughs for now), and formulates a final response. Routing is
deterministic and testable without an LLM or a live database.
"""

import pytest

from app.schemas.response import AgentStatus
from app.workflows.concierge_workflow import build_concierge_graph, run_concierge


def _invoke(message: str, org_context: dict | None = None) -> dict:
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
    return graph.invoke(initial)


def test_item_search_routes_through_memory_and_visual():
    result = _invoke("Do you have a blue saree for a wedding?")
    assert result["intent"]["intent_type"] == "item_search"
    assert result["memory_output"] is not None
    assert result["visual_output"] is not None
    assert result["commerce_output"] is None
    assert result["response"]["status"] == AgentStatus.success


def test_pricing_query_routes_through_memory_and_commerce():
    result = _invoke("How much is this dress?")
    assert result["intent"]["intent_type"] == "pricing_query"
    assert result["memory_output"] is not None
    assert result["visual_output"] is None
    assert result["commerce_output"] is not None


def test_out_of_scope_short_circuits():
    result = _invoke("Write me a python script to sort a list")
    assert result["intent"]["intent_type"] == "out_of_scope"
    assert result["intent"]["is_relevant"] is False
    # No agent should run for out-of-scope input.
    assert result["memory_output"] is None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None
    assert result["response"]["status"] == AgentStatus.out_of_scope


def test_general_inquiry_runs_memory_only():
    result = _invoke("Hello, how are you?")
    assert result["intent"]["intent_type"] == "general_inquiry"
    assert result["memory_output"] is not None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None


@pytest.mark.asyncio
async def test_run_concierge_returns_response():
    response = await run_concierge("Do you have a blue saree?")
    assert response.status == AgentStatus.success
    assert response.output is not None


@pytest.mark.asyncio
async def test_run_concierge_out_of_scope():
    response = await run_concierge("Tell me a joke")
    assert response.status == AgentStatus.out_of_scope
