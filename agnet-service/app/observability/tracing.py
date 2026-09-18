"""OpenTelemetry setup for the agent service.

Traditional APM breaks for AI agents because a single request can last 30-120s
with multiple LLM calls and tool executions. This module configures the
OpenTelemetry SDK with FastAPI, HTTPX and LangChain auto-instrumentation plus an
OTLP exporter, and exposes a ``chain_of_thought_span`` helper for tracing the
reasoning steps that auto-instrumentation cannot capture.

Span content (prompt/completion text) is stripped when ``OTEL_TRACE_CONTENT`` is
``false`` so secrets and customer data never leave the process in production.
"""

import logging
from contextlib import contextmanager
from typing import Any

from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor
from opentelemetry.instrumentation.langchain import LangchainInstrumentor
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.semconv.resource import ResourceAttributes

from app.core.config import Settings, get_settings

logger = logging.getLogger("aveline.agent.observability")

_provider: TracerProvider | None = None
_initialized = False


def init_tracing(settings: Settings | None = None) -> TracerProvider | None:
    """Configure the OpenTelemetry SDK and auto-instrumentation once.

    Idempotent: subsequent calls are no-ops. When no OTLP endpoint is configured
    the SDK is still initialized (spans go to the default console/logging
    pipeline) so local development and tests remain traceable.

    Args:
        settings: Optional settings override (defaults to the cached settings).

    Returns:
        The configured ``TracerProvider`` on first call, else ``None``.
    """
    global _provider, _initialized
    if _initialized:
        return None

    settings = settings or get_settings()
    resource = Resource.create(
        {ResourceAttributes.SERVICE_NAME: settings.otel_service_name}
    )
    provider = TracerProvider(resource=resource)

    if settings.otel_exporter_otlp_endpoint:
        exporter = OTLPSpanExporter(endpoint=settings.otel_exporter_otlp_endpoint)
        provider.add_span_processor(BatchSpanProcessor(exporter))
        logger.info(
            "OTLP trace exporter configured (endpoint=%s).",
            settings.otel_exporter_otlp_endpoint,
        )

    trace.set_tracer_provider(provider)
    FastAPIInstrumentor().instrument()
    HTTPXClientInstrumentor().instrument()
    LangchainInstrumentor().instrument()

    _provider = provider
    _initialized = True
    logger.info("OpenTelemetry initialized (service=%s).", settings.otel_service_name)
    return provider


@contextmanager
def chain_of_thought_span(
    name: str,
    *,
    query: str | None = None,
    model: str | None = None,
    input_messages: str | None = None,
    output_messages: str | None = None,
    input_tokens: int | None = None,
    output_tokens: int | None = None,
) -> Any:
    """Open a manual span capturing a single chain-of-thought step.

    Sets ``gen_ai.*``, ``llm.*`` and ``agent.*`` attributes following the
    OpenTelemetry GenAI semantic conventions. Prompt/completion content is only
    recorded when ``OTEL_TRACE_CONTENT`` is enabled.

    Args:
        name: Span name describing the reasoning step.
        query: The user query driving the agent.
        model: The LLM model in use.
        input_messages: Prompt content (stripped when trace content disabled).
        output_messages: Completion content (stripped when trace content disabled).
        input_tokens: Prompt token count.
        output_tokens: Completion token count.

    Yields:
        The active ``Span``.
    """
    settings = get_settings()
    tracer = trace.get_tracer("aveline.agent")
    with tracer.start_as_current_span(name) as span:
        if query is not None:
            span.set_attribute("agent.query", query)
        if model is not None:
            span.set_attribute("agent.model", model)
        if input_tokens is not None:
            span.set_attribute("llm.prompt_tokens", input_tokens)
        if output_tokens is not None:
            span.set_attribute("llm.completion_tokens", output_tokens)
        if settings.otel_trace_content:
            if input_messages is not None:
                span.set_attribute("gen_ai.input.messages", input_messages)
            if output_messages is not None:
                span.set_attribute("gen_ai.output.messages", output_messages)
        yield span
