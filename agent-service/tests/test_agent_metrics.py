"""Slice 4a — the agent ``MeterProvider``, the OTLP transport and the additive instruments.

R-6's mitigation is the acceptance criterion, not an optional extra: ``test_metrics_are_recorded``
drives a real ``/agents/query`` and asserts a **non-zero counter**. An existence-only test would
also have passed for ``TelemetryCollector`` and ``chain_of_thought_span``, which were dead code
before Slice 4b, so existence is necessary but not sufficient.

The instruments are the plan's §6.6 additive set only. An input/output token counter is
deliberately absent: ``opentelemetry-instrumentation-langchain`` already emits
``gen_ai.client.token.usage`` and a second measure of the same quantity is a defect (R-15).
"""

import os

import pytest
from fastapi.testclient import TestClient
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import InMemoryMetricReader

from app.api import agents
from app.core.config import Settings, get_settings
from app.main import app
from app.observability import metrics as metrics_module
from app.observability.metrics import (
    AGENT_METER_NAME,
    FORBIDDEN_LABEL_KEYS,
    INSTRUMENTS,
    TEMPORALITY_ENV_VAR,
    AgentMetrics,
    build_meter_provider,
    resolve_metrics_endpoint,
)

TEST_INTERNAL_TOKEN = "test-internal-token"
ORG_ID = "11111111-1111-1111-1111-111111111111"

EXPECTED_INSTRUMENTS = {
    "aveline.agent.run.duration",
    "aveline.agent.run.count",
    "aveline.agent.node.duration",
    "aveline.agent.node.failures",
    "aveline.agent.tool.calls",
    "aveline.agent.retries",
    "aveline.agent.run.unattributed",
    "aveline.agent.stream.runs",
    "aveline.agent.llm.tokens.cached",
}


@pytest.fixture(autouse=True)
def _configure_settings(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_TOKEN", TEST_INTERNAL_TOKEN)
    monkeypatch.setenv("AGENT_LLM_ENABLED", "false")
    monkeypatch.delenv("OTEL_EXPORTER_OTLP_ENDPOINT", raising=False)
    monkeypatch.delenv("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", raising=False)
    get_settings.cache_clear()
    yield
    os.environ.pop(TEMPORALITY_ENV_VAR, None)
    get_settings.cache_clear()


@pytest.fixture
def metric_reader():
    """Install an in-memory ``AgentMetrics`` and collect from it after the test."""
    reader = InMemoryMetricReader()
    provider = MeterProvider(metric_readers=[reader])
    metrics_module.set_agent_metrics(AgentMetrics(provider.get_meter(AGENT_METER_NAME)))
    yield reader
    metrics_module.set_agent_metrics(None)
    provider.shutdown()


def _metrics_by_name(reader: InMemoryMetricReader) -> dict:
    data = reader.get_metrics_data()
    collected: dict = {}
    assert data is not None
    for resource_metrics in data.resource_metrics:
        for scope_metrics in resource_metrics.scope_metrics:
            for metric in scope_metrics.metrics:
                collected[metric.name] = metric
    return collected


def test_instrument_set_is_exactly_the_additive_set():
    assert {spec.name for spec in INSTRUMENTS} == EXPECTED_INSTRUMENTS


def test_every_instrument_exists_with_exactly_the_declared_label_keys(metric_reader):
    agent_metrics = metrics_module.get_agent_metrics()
    agent_metrics.record_run(workflow="concierge", status="Succeeded", duration_s=1.25)
    agent_metrics.record_node(node="intent_gate", duration_s=0.05)
    agent_metrics.record_node_failure(node="commerce_agent", error_class="RuntimeError")
    agent_metrics.record_tool_call(tool="lookup_customers", status="Succeeded")
    agent_metrics.record_retry(node="memory_agent")
    agent_metrics.record_unattributed_run()
    agent_metrics.record_stream_run()
    agent_metrics.record_cached_tokens(provider="deepseek", model="deepseek-chat", count=42)

    collected = _metrics_by_name(metric_reader)
    assert EXPECTED_INSTRUMENTS <= set(collected)

    for spec in INSTRUMENTS:
        data_points = collected[spec.name].data.data_points
        assert data_points, f"{spec.name} recorded no data point"
        assert set(data_points[0].attributes) == set(spec.label_keys), spec.name


def test_no_instrument_declares_a_forbidden_label_key():
    for spec in INSTRUMENTS:
        for label_key in spec.label_keys:
            assert label_key.lower() not in FORBIDDEN_LABEL_KEYS, spec.name


def test_resolve_metrics_endpoint_follows_otel_precedence():
    both = Settings(
        otel_exporter_otlp_endpoint="http://base:4318",
        otel_exporter_otlp_metrics_endpoint="http://metrics:4318/custom",
    )
    assert (
        resolve_metrics_endpoint("http://explicit:4318/v1/metrics", both)
        == "http://explicit:4318/v1/metrics"
    )
    # OTEL_EXPORTER_OTLP_METRICS_ENDPOINT is used verbatim, not suffixed.
    assert resolve_metrics_endpoint(None, both) == "http://metrics:4318/custom"
    # The generic endpoint gets the metrics path appended.
    base_only = Settings(otel_exporter_otlp_endpoint="http://base:4318")
    assert resolve_metrics_endpoint(None, base_only) == "http://base:4318/v1/metrics"
    assert resolve_metrics_endpoint(None, Settings()) is None


def test_build_meter_provider_is_disabled_without_an_endpoint():
    assert build_meter_provider(Settings()) is None


def test_init_metrics_installs_the_provider_with_cumulative_temporality(monkeypatch):
    monkeypatch.setattr(metrics_module, "_initialized", False)
    monkeypatch.setattr(metrics_module, "_provider", None)
    monkeypatch.setattr(metrics_module, "_agent_metrics", None)
    installed: dict = {}
    monkeypatch.setattr(
        metrics_module.metrics, "set_meter_provider", lambda provider: installed.update(p=provider)
    )

    reader = InMemoryMetricReader()
    provider = metrics_module.init_metrics(
        Settings(otel_service_name="aveline-agent-service"), metric_readers=[reader]
    )

    assert isinstance(provider, MeterProvider)
    assert installed["p"] is provider
    # Cumulative temporality is set explicitly so it cannot be flipped downstream (Prometheus).
    assert os.environ[TEMPORALITY_ENV_VAR] == "CUMULATIVE"


def test_metrics_are_recorded(metric_reader, monkeypatch):
    """R-6's mitigation — a real query must move a counter, not merely register an instrument."""

    async def noop_usage(**kwargs):
        return None

    async def noop_run(payload, **kwargs):
        return None

    monkeypatch.setattr(agents, "report_usage", noop_usage)
    monkeypatch.setattr(agents, "report_agent_run", noop_run)

    response = TestClient(app).post(
        "/agents/query",
        headers={"X-Internal-Token": TEST_INTERNAL_TOKEN},
        json={
            "query": "Hello, just checking in.",
            "org_context": {"organization_id": ORG_ID},
        },
    )
    assert response.status_code == 200

    collected = _metrics_by_name(metric_reader)
    run_count = collected["aveline.agent.run.count"]
    total = sum(point.value for point in run_count.data.data_points)
    assert total >= 1

    assert collected["aveline.agent.run.duration"].data.data_points
    assert collected["aveline.agent.node.duration"].data.data_points


def test_agent_metrics_are_push_only_and_the_route_is_not_exposed():
    """No inbound metrics port was added (D4 = OTLP push; N-7)."""
    assert TestClient(app).get("/metrics").status_code == 404
