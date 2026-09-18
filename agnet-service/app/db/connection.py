"""Async SQLAlchemy engine and session factory for the agent service.

The agent service is a read/write consumer of the same PostgreSQL database owned
by Aveline.Api (ADR-001, ADR-003). Migrations are owned by EF Core on the .NET
side; this module only manages connections and sessions.
"""

import logging

from sqlalchemy import text
from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)

from app.core.config import get_settings

logger = logging.getLogger("aveline.agent.db")

_engine: AsyncEngine | None = None
_session_factory: async_sessionmaker[AsyncSession] | None = None


def create_engine(database_url: str | None = None) -> AsyncEngine:
    """Create an async SQLAlchemy engine for the configured database URL.

    Args:
        database_url: Optional override; defaults to ``DATABASE_URL``.

    Returns:
        A configured ``AsyncEngine``.
    """
    url = database_url or get_settings().database_url
    return create_async_engine(url, pool_pre_ping=True)


def create_session_factory(engine: AsyncEngine) -> async_sessionmaker[AsyncSession]:
    """Create an async session factory bound to the given engine."""
    return async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)


def get_engine() -> AsyncEngine:
    """Return the process-wide engine, creating it lazily on first use."""
    global _engine
    if _engine is None:
        _engine = create_engine()
        logger.info("Async engine created for %s.", _engine.url.render_as_string(hide_password=True))
    return _engine


def get_session_factory() -> async_sessionmaker[AsyncSession]:
    """Return the process-wide session factory, creating it lazily."""
    global _session_factory
    if _session_factory is None:
        _session_factory = create_session_factory(get_engine())
    return _session_factory


async def check_db_connection() -> bool:
    """Return whether the database is reachable (used by the readiness probe).

    Raises:
        Exception: If the database cannot be reached.
    """
    engine = get_engine()
    async with engine.connect() as conn:
        await conn.execute(text("SELECT 1"))
    return True
