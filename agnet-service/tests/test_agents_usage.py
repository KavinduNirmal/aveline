"""Tests for always-on usage reporting wiring (Issue #165).

``_report_usage_best_effort`` translates an ``AgentResponse.metadata`` into a call to the
backend ``/internal/usage/record`` endpoint. Rule-based runs (no LLM) report the sentinel
``provider/model = rule-based`` with zero tokens; LLM runs report the configured provider/model
and the captured token split. A reporting failure must never fail the agent query.
"""

import pytest

from app.api import agents
from app.core.config import get_settings
from app.schemas.response import AgentMetadata, AgentResponse, AgentStatus


@pytest.fixture(autouse=True)
def _configure_settings(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_TOKEN", "test-token")
    monkeypatch.setenv("API_BASE_URL", "http://localhost:5000")
    monkeypatch.setenv("LLM_PROVIDER", "deepseek")
    monkeypatch.setenv("LLM_MODEL", "deepseek-v4-flash")
    monkeypatch.setenv("AGENT_LLM_ENABLED", "false")
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def _response_with_metadata(model, input_tokens=None, output_tokens=None) -> AgentResponse:
    return AgentResponse(
        status=AgentStatus.success,
        output={},
        metadata=AgentMetadata(model=model, input_tokens=input_tokens, output_tokens=output_tokens),
    )


@pytest.mark.asyncio
async def test_rule_based_run_reports_rule_based_sentinel(monkeypatch):
    calls = []

    async def fake(**kwargs):
        calls.append(kwargs)

    monkeypatch.setattr(agents, "report_usage", fake)
    await agents._report_usage_best_effort(
        _response_with_metadata("rule-based", 0, 0), "org-1", "wf-1", "req-1"
    )

    assert len(calls) == 1
    assert calls[0]["organization_id"] == "org-1"
    assert calls[0]["workflow_id"] == "wf-1"
    assert calls[0]["request_id"] == "req-1"
    assert calls[0]["provider"] == "rule-based"
    assert calls[0]["model"] == "rule-based"
    assert calls[0]["input_tokens"] == 0
    assert calls[0]["output_tokens"] == 0


@pytest.mark.asyncio
async def test_llm_run_reports_configured_provider_and_tokens(monkeypatch):
    calls = []

    async def fake(**kwargs):
        calls.append(kwargs)

    monkeypatch.setattr(agents, "report_usage", fake)
    await agents._report_usage_best_effort(
        _response_with_metadata("deepseek-v4-flash", 120, 40), "org-1", "wf-1", "req-1"
    )

    assert calls[0]["provider"] == "deepseek"
    assert calls[0]["model"] == "deepseek-v4-flash"
    assert calls[0]["input_tokens"] == 120
    assert calls[0]["output_tokens"] == 40


@pytest.mark.asyncio
async def test_report_failure_is_swallowed(monkeypatch):
    async def boom(**kwargs):
        raise RuntimeError("usage endpoint down")

    monkeypatch.setattr(agents, "report_usage", boom)
    # Must not raise even though reporting failed.
    await agents._report_usage_best_effort(
        _response_with_metadata("rule-based", 0, 0), "org-1", "wf-1", "req-1"
    )


@pytest.mark.asyncio
async def test_no_metadata_skips_report(monkeypatch):
    calls = []

    async def fake(**kwargs):
        calls.append(kwargs)

    monkeypatch.setattr(agents, "report_usage", fake)
    response = AgentResponse(status=AgentStatus.success, output={})
    await agents._report_usage_best_effort(response, "org-1", "wf-1", "req-1")
    assert calls == []
