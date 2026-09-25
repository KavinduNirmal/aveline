"""Shared test configuration.

The suite must be hermetic: a unit test that silently reaches a real LLM provider is slow, costs
money, is non-deterministic, and fails in CI where no key exists. ``Settings`` reads the repo's
``.env`` (which carries a real ``LLM_API_KEY`` in development), so the default has to be turned
off explicitly rather than assumed off.

Tests that exercise an LLM inject a double (or enable it themselves with their own monkeypatch and
a ``get_settings.cache_clear()``), so this only closes the accidental path.
"""

import pytest

from app.core.config import get_settings


@pytest.fixture(autouse=True)
def _disable_llm_by_default(monkeypatch):
    """Keep the workflow deterministic unless a test opts in.

    With the LLM off, ``workflow_llm_or_none`` returns ``None``: the supervisor falls back to the
    rule-based plan, compaction produces no summary, and drafting stays template-based. That is the
    same path CI and offline development take, so the default test run exercises it.
    """
    monkeypatch.setenv("AGENT_LLM_ENABLED", "false")
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()
