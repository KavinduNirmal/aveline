"""PostgreSQL-backed LangGraph checkpointer (ADR-002).

Workflow state must persist so an agent can pause for human approval and resume
exactly where it left off. Thread IDs correspond to conversation sessions and are
stored in ``Approval_Queue.thread_id`` for resume.

Two construction paths are provided:

- ``create_checkpointer`` uses ``AsyncPostgresSaver.from_conn_string``, which sets
  ``autocommit=True`` and ``row_factory=dict_row`` automatically.
- ``create_checkpointer_from_conn`` builds the psycopg connection manually. When
  building a connection yourself you MUST pass ``autocommit=True`` (omitting it
  makes ``.setup()`` silently fail) and ``row_factory=dict_row``.
"""

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

import psycopg
from langgraph.checkpoint.postgres.aio import AsyncPostgresSaver
from psycopg.rows import dict_row

from app.core.config import get_settings

logger = logging.getLogger("aveline.agent.checkpointer")


def _psycopg_url(conn_string: str | None) -> str:
    """Return a psycopg-compatible URL.

    ``DATABASE_URL`` uses the SQLAlchemy ``postgresql+asyncpg://`` dialect, which
    psycopg cannot parse. Strip the ``+asyncpg`` suffix for the checkpointer.
    """
    url = conn_string or get_settings().database_url
    if url.startswith("postgresql+asyncpg://"):
        return url.replace("postgresql+asyncpg://", "postgresql://", 1)
    return url


@asynccontextmanager
async def create_checkpointer(
    conn_string: str | None = None,
) -> AsyncIterator[AsyncPostgresSaver]:
    """Create a configured ``AsyncPostgresSaver`` from a connection string.

    ``from_conn_string`` sets ``autocommit=True`` and ``row_factory=dict_row``
    automatically. ``setup()`` is invoked so the checkpoint tables exist.

    Args:
        conn_string: Optional override; defaults to ``DATABASE_URL``.

    Yields:
        A ready-to-use ``AsyncPostgresSaver``.
    """
    url = _psycopg_url(conn_string)
    async with AsyncPostgresSaver.from_conn_string(url) as saver:
        await saver.setup()
        logger.debug("AsyncPostgresSaver ready (from connection string).")
        yield saver


@asynccontextmanager
async def create_checkpointer_from_conn(
    conn_string: str | None = None,
) -> AsyncIterator[AsyncPostgresSaver]:
    """Create a checkpointer from a manually-built psycopg async connection.

    Demonstrates the manual construction path. ``autocommit=True`` is REQUIRED
    (omitting it makes ``.setup()`` silently fail) and ``row_factory=dict_row``
    must be passed explicitly.

    Args:
        conn_string: Optional override; defaults to ``DATABASE_URL``.

    Yields:
        A ready-to-use ``AsyncPostgresSaver``.
    """
    url = _psycopg_url(conn_string)
    async with await psycopg.AsyncConnection.connect(
        url,
        autocommit=True,
        prepare_threshold=0,
        row_factory=dict_row,
    ) as conn:
        saver = AsyncPostgresSaver(conn=conn)
        await saver.setup()
        logger.debug("AsyncPostgresSaver ready (from manual connection).")
        yield saver
