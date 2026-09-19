"""Slice 4b — the step-record producer on the real graph/query path.

``TelemetryCollector`` (``app/telemetry/agent_telemetry.py``) and ``chain_of_thought_span``
(``app/observability/tracing.py``) had no application caller before this slice (G-13). This
test drives a real ``/agents/query`` and asserts that both are now exercised, that more than
one step row is produced, and that the step schema and the metric labels stay bounded.

The serialized step keys are asserted against the .NET ``AgentStepReportRequest`` contract
(``Aveline.Api/Modules/Statistics/DTOs/AgentRunIngestDtos.cs``): the backend is the source of
truth and the Python payload must match it exactly.
"""

import pytest
from fastapi.testclient import TestClient
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import InMemoryMetricReader

from app.api import agents
from app.core.config import get_settings
from app.main import app
from app.observability import metrics as metrics_module
from app.observability.metrics import AGENT_METER_NAME, FORBIDDEN_LABEL_KEYS, AgentMetrics
from app.telemetry.agent_telemetry import AGENT_STEP_KINDS, TelemetryCollector
from app.workflows import concierge_workflow

TEST_INTERNAL_TOKEN = "test-internal-token"
ORG_ID = "11111111-1111-1111-1111-111111111111"

#: BR-5.5 — the only agent keys the backend ingest accepts.
REGISTERED_AGENT_KEYS = {"customer_memory", "visual_insight", "commerce", "orchestrator"}

#: The concierge graph's top-level nodes (bounded label values).
CONCIERGE_NODE_NAMES = {
    "intent_gate",
    "resolve_customer",
    "memory_agent",
    "visual_agent",
    "commerce_agent",
    "formulate_response",
}

#: The .NET ``AgentStepStatus`` enum (the ingest contract). There is no ``Running`` member.
AGENT_STEP_STATUSES = {"Succeeded", "Failed", "Skipped", "TimedOut"}

#: The .NET ``AgentStepReportRequest`` properties, camelCased as the wire contract.
EXPECTED_STEP_KEYS = {
    "stepIndex",
    "agentKey",
    "nodeName",
    "stepKind",
    "toolName",
    "status",
    "attemptNumber",
    "startedAt",
    "completedAt",
    "durationMs",
    "provider",
    "model",
    "inputTokens",
    "outputTokens",
    "cachedTokens",
    "actualCostUsd",
    "argsHash",
    "resultBytes",
    "errorCode",
}


@pytest.fixture(autouse=True)
def _configure_settings(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_TOKEN", TEST_INTERNAL_TOKEN)
    monkeypatch.setenv("AGENT_LLM_ENABLED", "false")
    monkeypatch.delenv("OTEL_EXPORTER_OTLP_ENDPOINT", raising=False)
    monkeypatch.delenv("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", raising=False)
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


@pytest.fixture
def captured(monkeypatch):
    """Capture the ``report_agent_run`` payload without touching the network."""
    payloads: list[dict] = []

    async def noop_usage(**kwargs):
        return None

    async def capture_run(payload, **kwargs):
        payloads.append(payload)
        return None

    monkeypatch.setattr(agents, "report_usage", noop_usage)
    monkeypatch.setattr(agents, "report_agent_run", capture_run)
    return payloads


@pytest.fixture
def metric_reader():
    reader = InMemoryMetricReader()
    provider = MeterProvider(metric_readers=[reader])
    metrics_module.set_agent_metrics(AgentMetrics(provider.get_meter(AGENT_METER_NAME)))
    yield reader
    metrics_module.set_agent_metrics(None)
    provider.shutdown()


def _query() -> None:
    response = TestClient(app).post(
        "/agents/query",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json={
            "query": "Hello, just checking in.",
            "org_context": {"organization_id": ORG_ID},
        },
    )
    assert response.status_code == 200


def test_real_graph_run_writes_more_than_one_step(captured):
    _query()

    assert len(captured) == 1
    steps = captured[0]["steps"]
    assert len(steps) > 1
    assert [step["stepIndex"] for step in steps] == list(range(len(steps)))


def test_step_payload_keys_match_the_backend_request_model(captured):
    _query()

    for step in captured[0]["steps"]:
        assert set(step.keys()) == EXPECTED_STEP_KEYS


def test_step_kind_is_a_member_of_the_backend_enum(captured):
    """The .NET ``AgentStepKind`` is the contract and has no ``NodeTransition`` member."""
    _query()

    for step in captured[0]["steps"]:
        assert step["stepKind"] in AGENT_STEP_KINDS
        assert step["stepKind"] == "Decision"


def test_step_node_and_agent_values_are_bounded(captured):
    _query()

    for step in captured[0]["steps"]:
        assert step["nodeName"] in CONCIERGE_NODE_NAMES
        assert step["agentKey"] in REGISTERED_AGENT_KEYS
        assert step["stepKind"] in AGENT_STEP_KINDS
        assert step["status"] in AGENT_STEP_STATUSES
        # Tool rows are unrestricted rows, but a tool name must never be a raw path.
        if step["toolName"] is not None:
            assert "/" not in step["toolName"]


def test_chain_of_thought_span_has_a_real_caller_on_the_query_path(captured, monkeypatch):
    span_names: list[str] = []
    real_span = concierge_workflow.chain_of_thought_span

    def spy(name, **kwargs):
        span_names.append(name)
        return real_span(name, **kwargs)

    monkeypatch.setattr(concierge_workflow, "chain_of_thought_span", spy)
    _query()

    assert "agent.node.intent_gate" in span_names
    assert "agent.node.formulate_response" in span_names


def test_no_run_id_or_step_index_appears_as_a_metric_label(captured, metric_reader):
    _query()

    data = metric_reader.get_metrics_data()
    assert data is not None
    checked = 0
    for resource_metrics in data.resource_metrics:
        for scope_metrics in resource_metrics.scope_metrics:
            for metric in scope_metrics.metrics:
                for point in metric.data.data_points:
                    attributes = point.attributes or {}
                    checked += 1
                    for key in attributes:
                        assert key.lower() not in FORBIDDEN_LABEL_KEYS, metric.name
                    # ``workflow`` is the bounded workflow *name*, never the workflow/run id.
                    if "workflow" in attributes:
                        assert attributes["workflow"] == "concierge"
    assert checked > 0


def test_finalize_closes_a_step_left_running():
    """An interrupted step must not reach the backend as ``Running`` (not an AgentStepStatus)."""
    collector = TelemetryCollector(workflow_id="wf-paused")
    collector.start_step("memory_agent")

    run = collector.finalize(status="PausedForApproval")

    step = run.steps[0]
    assert step.status in AGENT_STEP_STATUSES
    assert step.status == "Skipped"
    assert step.completed_at is not None
