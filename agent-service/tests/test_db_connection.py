import os

import pytest
from sqlalchemy.ext.asyncio import AsyncEngine, async_sessionmaker

from app.core.config import get_settings
from app.db.connection import create_engine, create_session_factory, get_engine, get_session_factory


@pytest.fixture(autouse=True)
def _clear_settings_cache():
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_create_engine_uses_database_url(monkeypatch):
    monkeypatch.setenv("DATABASE_URL", "postgresql+asyncpg://u:p@localhost:5432/db")
    get_settings.cache_clear()

    engine = create_engine()

    assert isinstance(engine, AsyncEngine)
    assert engine.url.drivername == "postgresql+asyncpg"
    assert engine.url.host == "localhost"


def test_create_engine_accepts_explicit_url():
    engine = create_engine("postgresql+asyncpg://u:p@localhost:5432/other")
    assert engine.url.database == "other"


def test_create_session_factory_returns_async_sessionmaker():
    engine = create_engine("postgresql+asyncpg://u:p@localhost:5432/db")
    factory = create_session_factory(engine)
    assert isinstance(factory, async_sessionmaker)


def test_get_engine_is_singleton(monkeypatch):
    monkeypatch.setenv("DATABASE_URL", "postgresql+asyncpg://u:p@localhost:5432/db")
    get_settings.cache_clear()
    assert get_engine() is get_engine()


def test_get_session_factory_is_singleton(monkeypatch):
    monkeypatch.setenv("DATABASE_URL", "postgresql+asyncpg://u:p@localhost:5432/db")
    get_settings.cache_clear()
    assert get_session_factory() is get_session_factory()


@pytest.mark.asyncio
async def test_engine_connects_to_live_database():
    """Smoke test against a reachable Postgres; skipped when none is available."""
    url = os.getenv("TEST_DATABASE_URL")
    if not url:
        pytest.skip("TEST_DATABASE_URL not set; skipping live DB smoke test")

    engine = create_engine(url)
    async with engine.connect() as conn:
        result = await conn.execute(__import__("sqlalchemy").text("SELECT 1"))
        assert result.scalar() == 1
    await engine.dispose()
