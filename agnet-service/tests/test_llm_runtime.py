"""Tests for the runtime LLM gate (app/llm/runtime.py).

``memory_llm_or_none`` decides whether an LLM is available for the memory agent's draft
generation. It returns ``None`` (deterministic fallback) unless explicitly enabled AND a key +
model are configured — so CI and local dev without keys stay fully rule-based.
"""

import pytest

from app.core.config import Settings
from app.llm.runtime import memory_llm_or_none


def test_disabled_by_flag_returns_none():
    settings = Settings(agent_llm_enabled=False, llm_provider="openai", llm_api_key="k", llm_model="m")
    assert memory_llm_or_none(settings) is None


def test_enabled_but_no_api_key_returns_none():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="", llm_model="m")
    assert memory_llm_or_none(settings) is None


def test_enabled_but_no_model_returns_none():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="k", llm_model="")
    assert memory_llm_or_none(settings) is None


def test_enabled_with_key_and_model_returns_chat_model():
    settings = Settings(agent_llm_enabled=True, llm_provider="openai", llm_api_key="k", llm_model="gpt-4o")
    model = memory_llm_or_none(settings)
    assert model is not None
    assert model.__class__.__name__ == "ChatOpenAI"


@pytest.mark.parametrize(
    "provider,expected",
    [("openai", "ChatOpenAI"), ("deepseek", "ChatDeepSeek")],
)
def test_enabled_respects_provider(provider, expected):
    settings = Settings(agent_llm_enabled=True, llm_provider=provider, llm_api_key="k", llm_model="m")
    assert memory_llm_or_none(settings).__class__.__name__ == expected
