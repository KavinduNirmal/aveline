"""Tests for the runtime LLM gate (app/llm/runtime.py).

``commerce_llm_or_none``, ``memory_llm_or_none``, and ``visual_llm_or_none`` decide whether an LLM is available for
agent workflow passes. They return ``None`` (deterministic fallback) unless explicitly enabled
AND a key + model are configured — so CI and local dev without keys stay fully rule-based.
"""

import pytest

from app.core.config import Settings
from app.llm.runtime import commerce_llm_or_none, memory_llm_or_none, visual_llm_or_none


def test_disabled_by_flag_returns_none():
    settings = Settings(agent_llm_enabled=False, llm_provider="openai", llm_api_key="k", llm_model="m")
    assert commerce_llm_or_none(settings) is None
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_enabled_but_no_api_key_returns_none():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="", llm_model="m")
    assert commerce_llm_or_none(settings) is None
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_enabled_but_no_model_returns_none():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="k", llm_model="")
    assert commerce_llm_or_none(settings) is None
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_enabled_with_key_and_model_returns_chat_model():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="k", llm_model="gpt-4o")
    comm_model = commerce_llm_or_none(settings)
    assert comm_model is not None
    assert comm_model.__class__.__name__ == "ChatOpenAI"

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
    assert commerce_llm_or_none(settings).__class__.__name__ == expected
    assert memory_llm_or_none(settings).__class__.__name__ == expected
    assert visual_llm_or_none(settings).__class__.__name__ == expected
