import pytest
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter

from app.core.config import get_settings
from app.observability import tracing


@pytest.fixture(scope="module", autouse=True)
def module_exporter():
    """Install a single in-memory provider for the whole module.

    OpenTelemetry only allows the global tracer provider to be set once, so all
    span-capturing tests share one provider/exporter and clear it between tests.
    """
    exporter = InMemorySpanExporter()
    provider = TracerProvider()
    provider.add_span_processor(SimpleSpanProcessor(exporter))
    trace.set_tracer_provider(provider)
    tracing._provider = provider
    tracing._initialized = True
    yield exporter


@pytest.fixture(autouse=True)
def _clear_state(module_exporter):
    module_exporter.clear()
    get_settings.cache_clear()
    yield
    module_exporter.clear()


def test_chain_of_thought_span_sets_attributes(module_exporter):
    with tracing.chain_of_thought_span(
        "reasoning.step",
        query="What is the margin?",
        model="deepseek-v4-flash",
        input_messages="user: hi",
        output_messages="assistant: hello",
        input_tokens=120,
        output_tokens=40,
    ):
        pass

    spans = module_exporter.get_finished_spans()
    assert len(spans) == 1
    span = spans[0]
    assert span.name == "reasoning.step"
    assert span.attributes["agent.query"] == "What is the margin?"
    assert span.attributes["agent.model"] == "deepseek-v4-flash"
    assert span.attributes["gen_ai.input.messages"] == "user: hi"
    assert span.attributes["gen_ai.output.messages"] == "assistant: hello"
    assert span.attributes["llm.prompt_tokens"] == 120
    assert span.attributes["llm.completion_tokens"] == 40


def test_chain_of_thought_span_strips_content_when_disabled(module_exporter, monkeypatch):
    monkeypatch.setenv("OTEL_TRACE_CONTENT", "false")
    get_settings.cache_clear()

    with tracing.chain_of_thought_span(
        "reasoning.step",
        query="What is the margin?",
        model="deepseek-v4-flash",
        input_messages="user: secret prompt",
        output_messages="assistant: secret completion",
        input_tokens=120,
        output_tokens=40,
    ):
        pass

    spans = module_exporter.get_finished_spans()
    assert len(spans) == 1
    span = spans[0]
    assert "gen_ai.input.messages" not in span.attributes
    assert "gen_ai.output.messages" not in span.attributes
    assert span.attributes["agent.query"] == "What is the margin?"
    assert span.attributes["agent.model"] == "deepseek-v4-flash"
    assert span.attributes["llm.prompt_tokens"] == 120
    assert span.attributes["llm.completion_tokens"] == 40


def test_chain_of_thought_span_omits_unset_attributes(module_exporter):
    with tracing.chain_of_thought_span("reasoning.step"):
        pass

    spans = module_exporter.get_finished_spans()
    assert len(spans) == 1
    assert "agent.query" not in spans[0].attributes
    assert "llm.prompt_tokens" not in spans[0].attributes


def test_init_tracing_is_idempotent(monkeypatch):
    monkeypatch.setattr(tracing, "_initialized", True)
    assert tracing.init_tracing() is None


def test_init_tracing_first_call_returns_provider(monkeypatch):
    monkeypatch.setattr(tracing, "_initialized", False)
    monkeypatch.setattr(tracing, "_provider", None)
    # Avoid touching the real global provider / instrumentors in tests.
    monkeypatch.setattr(tracing.trace, "set_tracer_provider", lambda provider: None)
    monkeypatch.setattr(tracing.FastAPIInstrumentor, "instrument", lambda *a, **k: None)
    monkeypatch.setattr(tracing.HTTPXClientInstrumentor, "instrument", lambda *a, **k: None)
    monkeypatch.setattr(tracing.LangchainInstrumentor, "instrument", lambda *a, **k: None)

    provider = tracing.init_tracing()

    assert provider is not None
    assert tracing._initialized is True
