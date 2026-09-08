from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict

# Defaults that are never acceptable for a shared service-to-service secret.
_WEAK_INTERNAL_TOKENS = {"", "change-me", "change-me-internal-token"}


class Settings(BaseSettings):
    """Application-wide settings, read from environment variables."""

    app_name: str = "Aveline Agent Service"
    version: str = "0.1.0"
    internal_api_token: str = ""
    api_base_url: str = "http://localhost:5000"
    redis_url: str = "redis://localhost:6379/0"
    # Event types this service subscribes to (Redis Pub/Sub, ADR-014).
    subscribe_event_types: list[str] = []

    # --- LLM provider (OpenAI or DeepSeek, switched at runtime) ---
    llm_provider: str = "openai"
    llm_api_key: str = ""
    llm_base_url: str = ""
    llm_model: str = ""

    # --- Database (PostgreSQL + pgvector) ---
    database_url: str = ""

    # --- Observability (OpenTelemetry) ---
    otel_exporter_otlp_endpoint: str = ""
    otel_service_name: str = "aveline-agent-service"
    # When False, prompt/completion content is stripped from exported spans.
    otel_trace_content: bool = True

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")


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
