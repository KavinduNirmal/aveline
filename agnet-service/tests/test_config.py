import pytest

from app.core.config import Settings, get_settings, validate_startup_settings


@pytest.fixture(autouse=True)
def _clear_settings_cache():
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_defaults_are_sane():
    settings = Settings()
    assert settings.app_name == "Aveline Agent Service"
    assert settings.llm_provider == "openai"
    assert settings.llm_api_key == ""
    assert settings.llm_base_url == ""
    assert settings.llm_model == ""
    assert settings.llm_thinking_enabled is False
    assert settings.agent_llm_enabled is True
    assert settings.agent_state_delay_ms == 0
    assert settings.database_url == ""
    assert settings.otel_service_name == "aveline-agent-service"
    assert settings.otel_trace_content is True


def test_env_vars_map_to_settings(monkeypatch):
    monkeypatch.setenv("LLM_PROVIDER", "deepseek")
    monkeypatch.setenv("LLM_API_KEY", "sk-test")
    monkeypatch.setenv("LLM_BASE_URL", "https://api.deepseek.com")
    monkeypatch.setenv("LLM_MODEL", "deepseek-v4-flash")
    monkeypatch.setenv("LLM_THINKING_ENABLED", "true")
    monkeypatch.setenv("AGENT_STATE_DELAY_MS", "1200")
    monkeypatch.setenv("DATABASE_URL", "postgresql+asyncpg://u:p@localhost:5432/db")
    monkeypatch.setenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://collector:4317")
    monkeypatch.setenv("OTEL_SERVICE_NAME", "aveline-agent-service")
    monkeypatch.setenv("OTEL_TRACE_CONTENT", "false")

    settings = Settings()

    assert settings.llm_provider == "deepseek"
    assert settings.llm_api_key == "sk-test"
    assert settings.llm_base_url == "https://api.deepseek.com"
    assert settings.llm_model == "deepseek-v4-flash"
    assert settings.llm_thinking_enabled is True
    assert settings.agent_state_delay_ms == 1200
    assert settings.database_url == "postgresql+asyncpg://u:p@localhost:5432/db"
    assert settings.otel_exporter_otlp_endpoint == "http://collector:4317"
    assert settings.otel_service_name == "aveline-agent-service"
    assert settings.otel_trace_content is False
    assert settings.otel_trace_content is False


def test_get_settings_is_cached(monkeypatch):
    monkeypatch.setenv("LLM_MODEL", "model-a")
    first = get_settings()
    monkeypatch.setenv("LLM_MODEL", "model-b")
    second = get_settings()
    assert first is second
    assert first.llm_model == "model-a"


def test_validate_startup_settings_accepts_strong_token():
    settings = Settings(internal_api_token="a-strong-secret-token-123")
    validate_startup_settings(settings)


def test_validate_startup_settings_rejects_empty_token():
    settings = Settings(internal_api_token="")
    with pytest.raises(RuntimeError):
        validate_startup_settings(settings)


def test_validate_startup_settings_rejects_weak_default_token():
    settings = Settings(internal_api_token="change-me-internal-token")
    with pytest.raises(RuntimeError):
        validate_startup_settings(settings)


def test_subscribe_event_types_empty_env_is_tolerated(monkeypatch):
    monkeypatch.setenv("SUBSCRIBE_EVENT_TYPES", "")
    assert Settings().subscribe_event_types == []


def test_subscribe_event_types_json_array(monkeypatch):
    monkeypatch.setenv("SUBSCRIBE_EVENT_TYPES", '["message.received"]')
    assert Settings().subscribe_event_types == ["message.received"]


def test_subscribe_event_types_comma_separated(monkeypatch):
    monkeypatch.setenv("SUBSCRIBE_EVENT_TYPES", "message.received,workflow.completed")
    assert Settings().subscribe_event_types == ["message.received", "workflow.completed"]
