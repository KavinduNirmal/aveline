"""OpenTelemetry metrics setup for the agent service (Slice 4a).

The agent pushes OTLP metrics to the collector, which gains a ``metrics:`` pipeline and a
``prometheus`` exporter; Prometheus scrapes the collector. There is deliberately **no**
``/metrics`` route on the agent and no new inbound port (D4 = OTLP push, N-7): the service
keeps ``expose:`` only in compose.

Two plan constraints are encoded here rather than left to a downstream default:

* **Cumulative temporality** (``OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=CUMULATIVE``)
  is set explicitly and the exporter's ``preferred_temporality`` is pinned too, so a delta
  exporter can never be introduced silently (Prometheus drops or mis-aggregates delta sums).
* **Semantic-convention stability** (``OTEL_SEMCONV_STABILITY_OPT_IN=http``) is set before the
  FastAPI/HTTPX instrumentors run, so the agent's HTTP metrics use the same new
  seconds-based names as the API (``http_server_request_duration_seconds``) rather than the
  old incubating millisecond names (R-17).

Endpoint precedence follows the OTel specification exactly: an explicit ``endpoint`` argument
wins, then ``OTEL_EXPORTER_OTLP_METRICS_ENDPOINT`` is used verbatim, then
``OTEL_EXPORTER_OTLP_ENDPOINT`` + ``/v1/metrics``. When none is configured the provider is not
built at all, so tests and local runs without a collector keep working (metrics are no-ops).

Instruments are the plan's §6.6 **additive** set only. There is deliberately no input/output
token counter: ``opentelemetry-instrumentation-langchain`` already emits
``gen_ai.client.token.usage``, and a second measure of the same quantity is a defect (R-15).
Only the cached-token direction is additive because cached tokens are span attributes today,
never a metric.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from typing import Literal

from opentelemetry import metrics
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
from opentelemetry.sdk.metrics import Counter, Histogram, MeterProvider, ObservableCounter
from opentelemetry.sdk.metrics.export import (
    AggregationTemporality,
    PeriodicExportingMetricReader,
)
from opentelemetry.sdk.resources import Resource
from opentelemetry.semconv.resource import ResourceAttributes

from app.core.config import Settings, get_settings

logger = logging.getLogger("aveline.agent.observability.metrics")

#: Instrumentation scope name for every agent instrument.
AGENT_METER_NAME = "aveline.agent"

#: R-17 — the semconv convention this service records. ``http`` opts into the stable
#: HTTP semantic conventions (seconds), matching Aveline.Api's scrape names.
SEMCONV_ENV_VAR = "OTEL_SEMCONV_STABILITY_OPT_IN"
SEMCONV_HTTP_VALUE = "http"

#: Explicit cumulative temporality so it cannot be flipped downstream (plan §4.4).
TEMPORALITY_ENV_VAR = "OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE"
TEMPORALITY_CUMULATIVE = "CUMULATIVE"

#: The metrics path appended to the generic OTLP endpoint (OTel spec).
METRICS_PATH = "/v1/metrics"

#: The collector's default OTLP/HTTP metrics port; only used for the export interval default.
DEFAULT_EXPORT_INTERVAL_MS = 60_000

#: Plan §7.3 — never use an unbounded id (or a raw path) as a metric label. Kept next to the
#: instruments so a new label is checked against it by ``test_agent_metrics.py``.
FORBIDDEN_LABEL_KEYS = frozenset(
    {
        "organization_id",
        "organizationid",
        "user_id",
        "userid",
        "customer_id",
        "customerid",
        "conversation_id",
        "conversationid",
        "message_id",
        "messageid",
        "workflow_id",
        "workflowid",
        "run_id",
        "runid",
        "api_key_id",
        "apikeyid",
        "step_index",
        "stepindex",
        "path",
        "request_path",
        "url",
        "http_target",
    }
)

_CUMULATIVE_TEMPORALITY = {
    Counter: AggregationTemporality.CUMULATIVE,
    Histogram: AggregationTemporality.CUMULATIVE,
    ObservableCounter: AggregationTemporality.CUMULATIVE,
}


@dataclass(frozen=True)
class InstrumentSpec:
    """One additive instrument: its name, type, unit and the only legal label keys."""

    name: str
    kind: Literal["counter", "histogram"]
    unit: str
    label_keys: tuple[str, ...]


#: The complete additive instrument set (plan §6.6). Order is documentation order.
INSTRUMENTS: tuple[InstrumentSpec, ...] = (
    InstrumentSpec("aveline.agent.run.duration", "histogram", "s", ("workflow", "status")),
    InstrumentSpec("aveline.agent.run.count", "counter", "1", ("workflow", "status")),
    InstrumentSpec("aveline.agent.node.duration", "histogram", "s", ("node",)),
    InstrumentSpec("aveline.agent.node.failures", "counter", "1", ("node", "error_class")),
    InstrumentSpec("aveline.agent.tool.calls", "counter", "1", ("tool", "status")),
    InstrumentSpec("aveline.agent.retries", "counter", "1", ("node",)),
    InstrumentSpec("aveline.agent.run.unattributed", "counter", "1", ()),
    InstrumentSpec("aveline.agent.stream.runs", "counter", "1", ()),
    InstrumentSpec("aveline.agent.llm.tokens.cached", "counter", "1", ("provider", "model")),
)

_provider: MeterProvider | None = None
_initialized = False
_agent_metrics: AgentMetrics | None = None


def configure_semconv_environment() -> None:
    """Record the semconv convention before any instrumentor runs (R-17).

    Uses ``setdefault`` so an operator's explicit opt-in is respected, while the default is
    the stable HTTP convention the API already uses. Call this from ``app.main`` at import
    time and from :func:`init_tracing` before the FastAPI/HTTPX instrumentors are applied.
    """
    os.environ.setdefault(SEMCONV_ENV_VAR, SEMCONV_HTTP_VALUE)


def resolve_metrics_endpoint(explicit: str | None, settings: Settings) -> str | None:
    """Resolve the OTLP/HTTP metrics endpoint using the OTel precedence rules.

    Args:
        explicit: An explicit endpoint argument; wins over every environment variable.
        settings: Application settings carrying both OTLP endpoint variables.

    Returns:
        The endpoint URL, or ``None`` when no endpoint is configured.
    """
    if explicit:
        return explicit
    if settings.otel_exporter_otlp_metrics_endpoint:
        # The metrics-specific variable is used verbatim (no path is appended).
        return settings.otel_exporter_otlp_metrics_endpoint
    if settings.otel_exporter_otlp_endpoint:
        return settings.otel_exporter_otlp_endpoint.rstrip("/") + METRICS_PATH
    return None


def build_meter_provider(
    settings: Settings | None = None,
    *,
    endpoint: str | None = None,
    metric_readers: list | None = None,
) -> MeterProvider | None:
    """Build a ``MeterProvider`` with an OTLP exporter, or ``None`` when disabled.

    Args:
        settings: Optional settings override (defaults to the cached settings).
        endpoint: Explicit endpoint override (tests, callers that resolve config themselves).
        metric_readers: Optional pre-built readers (e.g. an ``InMemoryMetricReader`` in
            tests). When supplied, no exporter is created and an endpoint is not required.

    Returns:
        The provider, or ``None`` when no endpoint is configured and no reader was supplied.
    """
    settings = settings or get_settings()
    resolved_endpoint = resolve_metrics_endpoint(endpoint, settings)

    if metric_readers is None and resolved_endpoint is None:
        logger.info("No OTLP metrics endpoint configured; agent metrics are disabled.")
        return None

    # Pin cumulative temporality before any exporter reads the environment (plan §4.4), so a
    # delta exporter can never be introduced silently downstream.
    os.environ[TEMPORALITY_ENV_VAR] = TEMPORALITY_CUMULATIVE

    if metric_readers is None:
        readers = [
            PeriodicExportingMetricReader(
                _build_metric_exporter(resolved_endpoint),
                export_interval_millis=DEFAULT_EXPORT_INTERVAL_MS,
            )
        ]
    else:
        readers = list(metric_readers)

    resource = Resource.create(
        {ResourceAttributes.SERVICE_NAME: settings.otel_service_name}
    )
    return MeterProvider(resource=resource, metric_readers=readers)


def _build_metric_exporter(endpoint: str) -> OTLPMetricExporter:
    """Build the OTLP/HTTP metric exporter with cumulative temporality pinned."""
    return OTLPMetricExporter(
        endpoint=endpoint,
        preferred_temporality=dict(_CUMULATIVE_TEMPORALITY),
    )


def init_metrics(
    settings: Settings | None = None,
    *,
    endpoint: str | None = None,
    metric_readers: list | None = None,
) -> MeterProvider | None:
    """Install the metrics ``MeterProvider`` once (idempotent).

    When no endpoint is configured the call is a no-op: existing instruments remain bound to
    the no-op proxy meter, so instrumentation is safe in tests and local runs.

    Args:
        settings: Optional settings override (defaults to the cached settings).
        endpoint: Explicit endpoint override.
        metric_readers: Optional pre-built readers (tests).

    Returns:
        The configured ``MeterProvider`` on first call, else ``None``.
    """
    global _provider, _initialized, _agent_metrics
    if _initialized:
        return None

    settings = settings or get_settings()
    provider = build_meter_provider(
        settings, endpoint=endpoint, metric_readers=metric_readers
    )
    if provider is None:
        _initialized = True
        return None

    metrics.set_meter_provider(provider)
    if _agent_metrics is None:
        _agent_metrics = AgentMetrics(provider.get_meter(AGENT_METER_NAME))
    _provider = provider
    _initialized = True
    logger.info(
        "OTLP metric exporter configured (endpoint=%s, temporality=%s).",
        resolve_metrics_endpoint(endpoint, settings),
        TEMPORALITY_CUMULATIVE,
    )
    return provider


def get_agent_metrics() -> AgentMetrics:
    """Return the process-wide :class:`AgentMetrics`, lazily bound to the global meter."""
    global _agent_metrics
    if _agent_metrics is None:
        _agent_metrics = AgentMetrics(metrics.get_meter(AGENT_METER_NAME))
    return _agent_metrics


def set_agent_metrics(agent_metrics: AgentMetrics | None) -> None:
    """Override the process-wide recorder (tests install an in-memory-backed instance)."""
    global _agent_metrics
    _agent_metrics = agent_metrics


class AgentMetrics:
    """The additive agent instruments, created from a single meter.

    Every method takes only bounded label values (plan §7.3): node names, tool names, status
    enums, error class names, provider/model identifiers and the workflow *name*. A run id,
    step index or organization id must travel as a row column, never as a label.
    """

    def __init__(self, meter: metrics.Meter) -> None:
        self.run_duration = meter.create_histogram(
            "aveline.agent.run.duration",
            unit="s",
            description="End-to-end agent workflow run duration in seconds.",
        )
        self.run_count = meter.create_counter(
            "aveline.agent.run.count",
            unit="1",
            description="Agent workflow runs by workflow name and terminal status.",
        )
        self.node_duration = meter.create_histogram(
            "aveline.agent.node.duration",
            unit="s",
            description="Per-node execution duration in seconds.",
        )
        self.node_failures = meter.create_counter(
            "aveline.agent.node.failures",
            unit="1",
            description="Node failures by node and error class.",
        )
        self.tool_calls = meter.create_counter(
            "aveline.agent.tool.calls",
            unit="1",
            description="Tool invocations by bounded tool name and status.",
        )
        self.retries = meter.create_counter(
            "aveline.agent.retries",
            unit="1",
            description="Node attempts beyond the first (retries).",
        )
        self.unattributed_runs = meter.create_counter(
            "aveline.agent.run.unattributed",
            unit="1",
            description="Runs with no resolvable organization; skipped by reporting today.",
        )
        self.stream_runs = meter.create_counter(
            "aveline.agent.stream.runs",
            unit="1",
            description="Runs served over the streaming query endpoint.",
        )
        self.cached_tokens = meter.create_counter(
            "aveline.agent.llm.tokens.cached",
            unit="1",
            description="Cached prompt tokens. The only additive token direction (R-15).",
        )

    def record_run(self, *, workflow: str, status: str, duration_s: float) -> None:
        """Record one completed (or failed) workflow run."""
        attributes = {"workflow": workflow, "status": status}
        self.run_count.add(1, attributes)
        self.run_duration.record(max(0.0, duration_s), attributes)

    def record_node(self, *, node: str, duration_s: float) -> None:
        """Record one node execution duration."""
        self.node_duration.record(max(0.0, duration_s), {"node": node})

    def record_node_failure(self, *, node: str, error_class: str) -> None:
        """Record one node failure by node and error class."""
        self.node_failures.add(1, {"node": node, "error_class": error_class})

    def record_tool_call(self, *, tool: str, status: str) -> None:
        """Record one tool invocation."""
        self.tool_calls.add(1, {"tool": tool, "status": status})

    def record_retry(self, *, node: str) -> None:
        """Record one node attempt beyond the first."""
        self.retries.add(1, {"node": node})

    def record_unattributed_run(self) -> None:
        """Record a run that could not be attributed to an organization."""
        self.unattributed_runs.add(1)

    def record_stream_run(self) -> None:
        """Record a run served over ``/agents/query/stream``."""
        self.stream_runs.add(1)

    def record_cached_tokens(self, *, provider: str, model: str, count: int) -> None:
        """Record cached prompt tokens for one provider/model pair."""
        if count <= 0:
            return
        self.cached_tokens.add(count, {"provider": provider, "model": model})
