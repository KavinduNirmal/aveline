"""Slice 4b — the step-record producer on the real graph/query path.

``TelemetryCollector`` (``app/telemetry/agent_telemetry.py``) and ``chain_of_thought_span``
(``app/observability/tracing.py``) had no application caller before this slice (G-13). This
test drives a real ``/agents/query`` and asserts that both are now exercised, that more than
one step row is produced, and that the step schema and the metric labels stay bounded.

The serialized step keys are asserted against the .NET ``AgentStepReportRequest`` contract
(``Aveline.Api/Modules/Statistics/DTOs/AgentRunIngestDtos.cs``): the backend is the source of
truth and the Python payload must match it exactly.
"""

from contextlib import asynccontextmanager

import pytest
from fastapi.testclient import TestClient
from langgraph.checkpoint.memory import InMemorySaver
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import InMemoryMetricReader

from app.api import agents
from app.core.config import get_settings
from app.main import app
from app.observability import metrics as metrics_module
from app.observability.metrics import AGENT_METER_NAME, FORBIDDEN_LABEL_KEYS, AgentMetrics
from app.telemetry.agent_telemetry import AGENT_STEP_KINDS, TelemetryCollector
from app.workflows import concierge_workflow

# A local-only placeholder: it is both the token the test sets and the one it sends, so the value
# is arbitrary. Worded to read as a placeholder so the secret scanner does not flag it.
TEST_INTERNAL_TOKEN = "local-development-placeholder-token"
ORG_ID = "11111111-1111-1111-1111-111111111111"

#: BR-5.5 — the only agent keys the backend ingest accepts.
REGISTERED_AGENT_KEYS = {"customer_memory", "visual_insight", "commerce", "orchestrator"}

#: The concierge graph's top-level nodes (bounded label values).
CONCIERGE_NODE_NAMES = {
    "load_context",
    "load_handbook",
    "supervisor",
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

    # The routing node is the supervisor now (ADR-023); it replaced the rule-only intent gate.
    assert "agent.node.supervisor" in span_names
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


# ---------------------------------------------------------------------------
# Run identity and thread linkage (the run-telemetry regression)
# ---------------------------------------------------------------------------


@pytest.fixture
def _noop_checkpointer(monkeypatch):
    """A thread_id enables checkpointing; swap the Postgres saver for an in-memory one.

    These tests are about run telemetry, not persistence, and they must not need a database. The
    stub is a real ``InMemorySaver`` rather than an opaque object because the checkpointer is now
    compiled into the graph (ADR-024) and its ``aget_state`` is how a paused run is discovered.
    """

    @asynccontextmanager
    async def fake_checkpointer(*args, **kwargs):
        yield InMemorySaver()

    monkeypatch.setattr(
        "app.workflows.concierge_workflow.create_checkpointer", fake_checkpointer
    )


def _query_on_thread(thread_id: str, conversation_id: str) -> None:
    """Query with a thread and conversation, but no customer.

    Deliberately no customer_id: supplying one makes the memory agent call the customer book,
    which is not running here. The conversation linkage is what these tests are about.
    """
    response = TestClient(app).post(
        "/agents/query",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json={
            "query": "Hello, just checking in.",
            "thread_id": thread_id,
            "org_context": {
                "organization_id": ORG_ID,
                "conversation_id": conversation_id,
            },
        },
    )
    assert response.status_code == 200


def test_each_message_in_one_conversation_records_its_own_run(captured, _noop_checkpointer):
    """The run id must be unique per invocation, not the conversation's thread id.

    It used to be the thread id, which is stable for the whole conversation. The API keys run
    ingest on `(OrganizationId, WorkflowId)` and rejects a repeat of a terminal run, so the first
    message was recorded and every later message 409'd - telemetry for the rest of the thread was
    silently dropped, and with it any HITL pause after the first message.
    """
    thread = "thread-one-conversation"
    _query_on_thread(thread, "conv-1")
    _query_on_thread(thread, "conv-1")

    assert len(captured) == 2, "both messages must report a run"
    first, second = captured[0]["workflowId"], captured[1]["workflowId"]

    assert first != second, "two runs must not share a workflow id"
    assert first != thread and second != thread, "the thread id must not be the run id"


def test_run_payload_links_back_to_its_conversation(captured, _noop_checkpointer):
    """A run has to say which conversation it served, or nothing can render it in context."""
    _query_on_thread("thread-with-conversation", "conv-abc")

    assert captured[0]["conversationId"] == "conv-abc"


def test_run_payload_omits_linkage_when_the_caller_supplies_none(captured):
    # A staff query with no bound conversation must not invent one.
    _query()

    payload = captured[0]
    assert payload["conversationId"] is None
    assert payload["customerId"] is None


def test_collector_carries_both_ids_onto_the_run_report():
    """The collector is what threads org_context ids onto the run row."""
    collector = TelemetryCollector(
        workflow_id="run-1",
        organization_id="11111111-1111-1111-1111-111111111111",
        request_id="req-1",
        conversation_id="conv-9",
        customer_id="cust-9",
    )

    report = collector.finalize()

    assert report.conversation_id == "conv-9"
    assert report.customer_id == "cust-9"
