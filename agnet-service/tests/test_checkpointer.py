import os
from contextlib import asynccontextmanager

import pytest

from app.workflows import checkpointer
from app.workflows.checkpointer import create_checkpointer, create_checkpointer_from_conn


class FakeSaver:
    def __init__(self) -> None:
        self.setup_calls = 0

    async def setup(self) -> None:
        self.setup_calls += 1


class FakeConnection:
    def __init__(self) -> None:
        self.closed = False

    async def __aenter__(self) -> "FakeConnection":
        return self

    async def __aexit__(self, *exc) -> None:
        self.closed = True


def test_psycopg_url_strips_asyncpg_dialect():
    assert (
        checkpointer._psycopg_url("postgresql+asyncpg://u:p@localhost:5432/db")
        == "postgresql://u:p@localhost:5432/db"
    )


def test_psycopg_url_passes_through_plain_postgres_url():
    assert checkpointer._psycopg_url("postgresql://u:p@localhost:5432/db") == "postgresql://u:p@localhost:5432/db"


@pytest.mark.asyncio
async def test_create_checkpointer_calls_setup_and_yields(monkeypatch):
    saver = FakeSaver()

    @asynccontextmanager
    async def fake_from_conn_string(url, **kwargs):
        yield saver

    monkeypatch.setattr(checkpointer.AsyncPostgresSaver, "from_conn_string", fake_from_conn_string)

    async with create_checkpointer("postgresql://u:p@localhost:5432/db") as yielded:
        assert yielded is saver
        assert saver.setup_calls == 1


@pytest.mark.asyncio
async def test_create_checkpointer_from_conn_uses_autocommit_and_dict_row(monkeypatch):
    saver = FakeSaver()
    conn = FakeConnection()
    captured = {}

    async def fake_connect(url, **kwargs):
        captured.update(kwargs)
        return conn

    monkeypatch.setattr(checkpointer.psycopg.AsyncConnection, "connect", fake_connect)
    monkeypatch.setattr(checkpointer.AsyncPostgresSaver, "__init__", lambda self, conn: None)
    monkeypatch.setattr(checkpointer.AsyncPostgresSaver, "setup", saver.setup)

    async with create_checkpointer_from_conn("postgresql://u:p@localhost:5432/db") as yielded:
        assert yielded is not None
        assert saver.setup_calls == 1

    assert captured["autocommit"] is True
    assert captured["row_factory"] is checkpointer.dict_row


@pytest.mark.asyncio
async def test_live_checkpointer_against_database():
    """Integration test against a reachable Postgres; skipped when unavailable."""
    url = os.getenv("TEST_DATABASE_URL")
    if not url:
        pytest.skip("TEST_DATABASE_URL not set; skipping checkpointer integration test")
    if url.startswith("postgresql+asyncpg://"):
        url = url.replace("postgresql+asyncpg://", "postgresql://", 1)

    async with create_checkpointer(url) as saver:
        checkpoints = [c async for c in saver.alist({"configurable": {"thread_id": "nonexistent"}})]
        assert checkpoints == []
