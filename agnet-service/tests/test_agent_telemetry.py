import os

import pytest
import respx

from app.core.config import get_settings
from app.services.usage_reporter import report_agent_run
from app.telemetry.agent_telemetry import (
    TelemetryCollector,
    hash_args,
    map_agent_key,
)


@pytest.fixture(autouse=True)
def _configure_settings():
    os.environ["INTERNAL_API_TOKEN"] = "test-token"
    os.environ["API_BASE_URL"] = "http://localhost:5000"
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_map_agent_key():
    assert map_agent_key("memory_agent") == "customer_memory"
    assert map_agent_key("visual_agent") == "visual_insight"
    assert map_agent_key("commerce_agent") == "commerce"
    assert map_agent_key("intent_gate") == "orchestrator"
    assert map_agent_key(None) == "orchestrator"


def test_hash_args():
    assert hash_args(None) is None
    h1 = hash_args({"query": "red dress"})
    assert isinstance(h1, str)
    assert len(h1) == 32
    assert hash_args({"query": "red dress"}) == h1


def test_telemetry_collector_lifecycle():
    collector = TelemetryCollector(
        workflow_id="wf-test-123",
        organization_id="11111111-1111-1111-1111-111111111111",
        request_id="req-abc",
    )

    collector.start_step("memory_agent", step_kind="Decision")
    collector.complete_current_step(status="Succeeded", input_tokens=100, output_tokens=50)

    collector.start_step("visual_agent", step_kind="ToolCall", tool_name="search_inventory")
    collector.complete_current_step(status="Succeeded", input_tokens=50, output_tokens=25)

    run = collector.finalize(status="Succeeded", input_tokens=150, output_tokens=75)

    assert run.workflow_id == "wf-test-123"
    assert run.status == "Succeeded"
    assert run.tool_call_count == 1
    assert run.retry_count == 0
    assert len(run.steps) == 2
    assert run.steps[0].agent_key == "customer_memory"
    assert run.steps[1].agent_key == "visual_insight"
    assert run.steps[1].tool_name == "search_inventory"

    payload = run.to_api_payload()
    assert payload["workflowId"] == "wf-test-123"
    assert payload["organizationId"] == "11111111-1111-1111-1111-111111111111"
    assert "steps" in payload
    assert len(payload["steps"]) == 2
    assert payload["steps"][0]["stepIndex"] == 0
    assert payload["steps"][0]["agentKey"] == "customer_memory"


@pytest.mark.asyncio
@respx.mock
async def test_report_agent_run_success():
    expected_response = {
        "id": "0191c49b-7345-7123-8901-23456789abcd",
        "organizationId": "11111111-1111-1111-1111-111111111111",
        "workflowId": "wf-test-1",
        "status": "Succeeded",
        "stepCount": 1,
        "created": True,
    }

    route = respx.post("http://localhost:5000/internal/agent-runs").respond(
        status_code=201, json=expected_response
    )

    payload = {
        "workflowId": "wf-test-1",
        "organizationId": "11111111-1111-1111-1111-111111111111",
        "status": "Succeeded",
        "steps": [],
    }

    result = await report_agent_run(payload)
    assert route.called
    request = route.calls.last.request
    assert request.headers["X-Internal-Token"] == "test-token"
    assert request.headers["Content-Type"] == "application/json"
    assert result == expected_response
