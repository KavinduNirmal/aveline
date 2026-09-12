"""Tests for the LLM runtime gate (app/llm/runtime.py)."""

from langchain_openai import ChatOpenAI

from app.core.config import Settings
from app.llm.runtime import memory_llm_or_none, visual_llm_or_none


def test_runtime_returns_none_when_disabled():
    settings = Settings(
        agent_llm_enabled=False,
        llm_provider="openai",
        llm_api_key="sk-fake",
        llm_model="gpt-4o-mini",
    )
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_runtime_returns_none_when_missing_key():
    settings = Settings(
        agent_llm_enabled=True,
        llm_provider="openai",
        llm_api_key="",
        llm_model="gpt-4o-mini",
    )
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_runtime_returns_none_when_missing_model():
    settings = Settings(
        agent_llm_enabled=True,
        llm_provider="openai",
        llm_api_key="sk-fake",
        llm_model="",
    )
    assert memory_llm_or_none(settings) is None
    assert visual_llm_or_none(settings) is None


def test_runtime_returns_chat_model_when_configured():
    settings = Settings(
        agent_llm_enabled=True,
        llm_provider="openai",
        llm_api_key="sk-fake",
        llm_model="gpt-4o-mini",
    )
    mem_llm = memory_llm_or_none(settings)
    assert isinstance(mem_llm, ChatOpenAI)

    vis_llm = visual_llm_or_none(settings)
    assert isinstance(vis_llm, ChatOpenAI)
