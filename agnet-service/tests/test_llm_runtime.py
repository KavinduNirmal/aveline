"""Tests for the runtime LLM gate (app/llm/runtime.py).

``memory_llm_or_none`` and ``visual_llm_or_none`` decide whether an LLM is available for
agent workflow passes. They return ``None`` (deterministic fallback) unless explicitly enabled
AND a key + model are configured — so CI and local dev without keys stay fully rule-based.
"""

import pytest

from app.core.config import Settings
from app.llm.runtime import memory_llm_or_none, visual_llm_or_none


def test_disabled_by_flag_returns_none():
    settings = Settings(agent_llm_enabled=False, llm_provider="openai", llm_api_key="k", llm_model="m")
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_enabled_but_no_api_key_returns_none():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="", llm_model="m")
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_enabled_but_no_model_returns_none():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="k", llm_model="")
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_enabled_with_key_and_model_returns_chat_model():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="k", llm_model="gpt-4o")
    mem_model = memory_llm_or_none(settings)
    assert mem_model is not None
    assert mem_model.__class__.__name__ == "ChatOpenAI"

    vis_model = visual_llm_or_none(settings)
    assert vis_model is not None
    assert vis_model.__class__.__name__ == "ChatOpenAI"


@pytest.mark.parametrize(
    "provider,expected",
    [("openai", "ChatOpenAI"), ("deepseek", "ChatDeepSeek")],
)
def test_enabled_respects_provider(provider, expected):
    settings = Settings(agent_llm_enabled=True, llm_provider=provider, llm_api_key="k", llm_model="m")
    assert memory_llm_or_none(settings).__class__.__name__ == expected
    assert visual_llm_or_none(settings).__class__.__name__ == expected
