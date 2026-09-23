import json
from functools import lru_cache
from typing import Annotated

from pydantic import field_validator
from pydantic_settings import BaseSettings, NoDecode, SettingsConfigDict

# Defaults that are never acceptable for a shared service-to-service secret.
_WEAK_INTERNAL_TOKENS = {"", "change-me", "change-me-internal-token"}


class Settings(BaseSettings):
    """Application-wide settings, read from environment variables."""

    app_name: str = "Aveline Agent Service"
    version: str = "0.1.0"
    internal_api_token: str = ""
    api_base_url: str = "http://localhost:5000"
    redis_url: str = "redis://localhost:6379/0"
    # Event types this service subscribes to (Redis Pub/Sub, ADR-014). NoDecode stops
    # pydantic-settings from JSON-parsing the env value so an empty string is tolerated.
    subscribe_event_types: Annotated[list[str], NoDecode] = []

    # --- LLM provider (OpenAI or DeepSeek, switched at runtime) ---
    llm_provider: str = "deepseek"
    llm_api_key: str = ""
    llm_base_url: str = "https://api.deepseek.com"
    llm_model: str = "deepseek-chat"
    # When False, DeepSeek reasoner-capable models are asked not to emit a thinking
    # (reasoning) pass. Only applied to the DeepSeek provider.
    llm_thinking_enabled: bool = False
    # Master switch for invoking an LLM in the running agent workflow. When True the
    # workflow uses an LLM only if LLM_API_KEY and LLM_MODEL are also configured; otherwise
    # it stays fully deterministic (rule-based), keeping CI/dev green without keys.
    agent_llm_enabled: bool = True

    # --- Agent workflow pacing ---
    # Artificial delay (ms) inserted between emitted lifecycle states so clients can
    # visibly animate Aveline's blossom through each state during integration testing.
    # 0 disables the delay (production-safe default).
    agent_state_delay_ms: int = 0

    # --- Conversation context window (ADR-023, W1) ---
    # How many transcript turns are fetched per run. Bounded because the window is a context
    # budget, not an archive: the full transcript stays in the API's Messages table.
    context_window_turns: int = 20
    # Approximate token ceiling for the transcript window before it is compacted into the
    # rolling thread summary. Measured in tokens rather than messages because one turn carrying
    # an image is not one turn of text.
    context_window_tokens: int = 2000

    # --- Handbook knowledge base (ADR-025) ---
    # The concierge's retrievable product/platform knowledge. Answers are grounded in retrieved
    # excerpts, so the lane is inert without an LLM: retrieval still runs, but nothing composes an
    # answer from it.
    handbook_enabled: bool = True
    # How many fused excerpts are put in front of the model.
    handbook_top_k: int = 5
    # Cosine floor, applied to the dense leg only. A floor after fusion would delete lexical-only
    # hits, which is the failure hybrid retrieval exists to fix.
    handbook_min_similarity: float = 0.0
    # staff | customer | both. The handbook is staff-facing today.
    handbook_audience: str = "staff"

    # --- Tenant account awareness (ADR-026) ---
    # Whether the concierge may fetch a boutique's own account figures (Blossom balance, seat and
    # customer allowances) so Aveline can answer questions about the tenant's plan. The fetch is
    # additionally gated per request on explicit staff evidence, so this flag can only widen a lane
    # that the caller's audience already opened.
    tenant_awareness_enabled: bool = True

    # --- Database (PostgreSQL + pgvector) ---
    database_url: str = ""

    # --- Observability (OpenTelemetry) ---
    otel_exporter_otlp_endpoint: str = ""
    # Metrics-specific OTLP endpoint (OTEL_EXPORTER_OTLP_METRICS_ENDPOINT). Used verbatim when
    # set; otherwise the generic endpoint above gets "/v1/metrics" appended (OTel precedence).
    otel_exporter_otlp_metrics_endpoint: str = ""
    otel_service_name: str = "aveline-agent-service"
    # When False, prompt/completion content is stripped from exported spans.
    otel_trace_content: bool = True

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    @field_validator("subscribe_event_types", mode="before")
    @classmethod
    def _parse_event_types(cls, value: object) -> object:
        """Tolerate an empty/whitespace env value (e.g. ``SUBSCRIBE_EVENT_TYPES=""``).

        An empty value simply means "subscribe to nothing". A non-empty value may be a
        JSON array (e.g. ``["message.received"]``) or a comma-separated list.
        """
        if isinstance(value, str):
            stripped = value.strip()
            if not stripped:
                return []
            if stripped.startswith("["):
                try:
                    return json.loads(stripped)
                except json.JSONDecodeError:
                    return []
            return [item.strip() for item in stripped.split(",") if item.strip()]
        return value


def validate_startup_settings(settings: Settings) -> None:
    """Fail fast on insecure configuration before the service starts serving.

    The agent service is only ever called by Aveline.Api over internal HTTP
    (ADR-009). A missing or placeholder ``INTERNAL_API_TOKEN`` must prevent the
    process from booting rather than failing lazily on the first request.
    """
    if settings.internal_api_token in _WEAK_INTERNAL_TOKENS:
        raise RuntimeError(
            "INTERNAL_API_TOKEN is not configured with a strong value; refusing to start."
        )


@lru_cache
def get_settings() -> Settings:
    return Settings()
