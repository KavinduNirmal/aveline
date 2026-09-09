"""Tests for the 3-layer prompt system (app/prompts/).

The universal system prompt is the single source of truth shipped with the agent
service at ``agnet-service/app/prompts/SYSTEM_PROMPT.md``; the loader reads it
once and caches it. Agent-specific prompts are placeholders owned by the slice
students.
"""

from pathlib import Path

import pytest

from app.prompts import agent_prompts, assembly, context, loader

# tests/test_prompt_system.py -> agnet-service/app/prompts/SYSTEM_PROMPT.md
CANONICAL_PROMPT = Path(__file__).resolve().parents[1] / "app" / "prompts" / "SYSTEM_PROMPT.md"


@pytest.fixture(autouse=True)
def _clear_loader_cache():
    loader.clear_cache()
    yield
    loader.clear_cache()


# ---------------------------------------------------------------------------
# loader
# ---------------------------------------------------------------------------


def test_canonical_prompt_file_exists():
    assert CANONICAL_PROMPT.exists(), "SYSTEM_PROMPT.md must exist at app/prompts/"


def test_load_system_prompt_returns_content():
    content = loader.load_system_prompt()
    assert isinstance(content, str)
    assert len(content) > 100


def test_load_system_prompt_is_cached(monkeypatch):
    calls = {"n": 0}

    def fake_read(path):
        calls["n"] += 1
        return "cached-content"

    monkeypatch.setattr(loader, "_read_file", fake_read)
    loader.load_system_prompt()
    loader.load_system_prompt()
    assert calls["n"] == 1


def test_load_system_prompt_raises_when_missing(tmp_path):
    missing = tmp_path / "does-not-exist.md"
    with pytest.raises(FileNotFoundError):
        loader.load_system_prompt(path=missing)


def test_load_system_prompt_accepts_explicit_path(tmp_path):
    f = tmp_path / "prompt.md"
    f.write_text("hello prompt")
    assert loader.load_system_prompt(path=f) == "hello prompt"


# ---------------------------------------------------------------------------
# agent_prompts
# ---------------------------------------------------------------------------


def test_agent_prompts_has_all_three_agents():
    assert set(agent_prompts.AGENT_PROMPTS.keys()) == {"memory", "visual", "commerce"}


def test_agent_prompts_are_placeholders():
    # Memory (Slice 1) is implemented; visual and commerce remain placeholders for Slice 2/3.
    for name in ("visual", "commerce"):
        assert "PLACEHOLDER" in agent_prompts.AGENT_PROMPTS[name], f"{name} prompt must be a placeholder"


def test_memory_prompt_is_implemented():
    prompt = agent_prompts.AGENT_PROMPTS["memory"]
    assert "PLACEHOLDER" not in prompt
    assert "Customer Memory Agent" in prompt
    assert "Ava" in prompt


# ---------------------------------------------------------------------------
# context
# ---------------------------------------------------------------------------


def test_build_customer_prompt_renders_plan_and_voice():
    org = {"plan_tier": "orchid", "brand_voice": "Elegant and formal"}
    prompt = context.build_customer_prompt(org)
    assert "orchid" in prompt
    assert "Elegant and formal" in prompt


def test_build_customer_prompt_renders_business_rules():
    org = {"business_rules": {"discount_cap": "10%", "approval_threshold": "50000"}}
    prompt = context.build_customer_prompt(org)
    assert "discount_cap" in prompt
    assert "approval_threshold" in prompt


def test_build_customer_prompt_handles_empty_context():
    prompt = context.build_customer_prompt({})
    assert isinstance(prompt, str)


# ---------------------------------------------------------------------------
# assembly
# ---------------------------------------------------------------------------


def test_assemble_contains_all_three_layers():
    org = {"plan_tier": "seed", "brand_voice": "Warm"}
    prompt = assembly.assemble_system_prompt("memory", org)
    # universal layer
    assert "Aveline" in prompt
    # agent layer
    assert "PLACEHOLDER" in prompt
    # context layer
    assert "seed" in prompt


def test_assemble_unknown_agent_raises():
    with pytest.raises(KeyError):
        assembly.assemble_system_prompt("unknown_agent", {})


def test_assemble_without_context():
    prompt = assembly.assemble_system_prompt("commerce")
    assert "Aveline" in prompt
