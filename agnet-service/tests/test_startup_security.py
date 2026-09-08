import pytest

from app.core.config import get_settings, validate_startup_settings
from app.main import lifespan


@pytest.fixture(autouse=True)
def _clear_settings_cache():
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


@pytest.mark.asyncio
async def test_lifespan_refuses_to_start_with_weak_token(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_TOKEN", "change-me-internal-token")
    get_settings.cache_clear()

    with pytest.raises(RuntimeError):
        async with lifespan(None):
            pass  # pragma: no cover - never reached


@pytest.mark.asyncio
async def test_lifespan_refuses_to_start_with_empty_token(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_TOKEN", "")
    get_settings.cache_clear()

    with pytest.raises(RuntimeError):
        async with lifespan(None):
            pass  # pragma: no cover - never reached


@pytest.mark.asyncio
async def test_validate_startup_settings_is_exported():
    assert callable(validate_startup_settings)
