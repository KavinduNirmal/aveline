"""Conversation-context transport into the specialist sub-graphs (ADR-023).

The concierge orchestrator loads one bounded transcript window and renders it into exactly one
prompt (the supervisor). These tests pin the second half of the feature: the same three layers
reach every specialist that owns a prompt.

Two defects made the follow-up case ("the pink one") fail, and each needs its own kind of test:

1. **The invocation.** The orchestrator's sub-graph calls built fixed dictionaries with no context
   key. ``tests/test_concierge_workflow.py`` covers that half.
2. **The schema.** LangGraph drops state keys a ``TypedDict`` does not declare, silently. A test
   that mocks the sub-graph cannot see this, so the round-trip is asserted inside the real
   sub-graphs (``tests/test_customer_memory_agent.py``, ``tests/agents/test_visual_insight_graph.py``)
   and the declarations themselves are guarded here.
"""

import pytest

from app.agents.commerce.state import CommerceAgentState
from app.agents.customer_memory.state import MemoryAgentState
from app.agents.visual_insight.state import VisualAgentState
from app.context import estimate_tokens, fit_to_budget, render_context_block, render_turns
from app.prompts.assembly import assemble_system_prompt
from app.workflows.concierge_workflow import run_load_context

#: The transport: the three layers, under the same names as ``ConciergeState``.
CONTEXT_FIELDS = ("history", "thread_summary", "pinned_slots")

PRIOR_TURN = {"authorKind": "Customer", "text": "Any pinkish gowns?"}


# ---------------------------------------------------------------------------
# render_turns / render_context_block
# ---------------------------------------------------------------------------


def test_render_turns_renders_author_and_text_oldest_first():
    turns = [
        {"authorKind": "Customer", "text": "Any pinkish gowns?"},
        {"authorKind": "Agent", "text": "We have three."},
    ]

    assert render_turns(turns) == "Customer: Any pinkish gowns?\nAgent: We have three."


def test_render_turns_is_empty_for_nothing_and_tolerates_sparse_turns():
    assert render_turns(None) == ""
    assert render_turns([]) == ""
    # A turn with no prose (an attachment-only message) must not raise: the renderer is reached
    # before anything validates the transcript's shape.
    assert render_turns([{"authorKind": "Customer"}]) == "Customer: None"


def test_render_context_block_is_empty_when_every_layer_is_empty():
    # This is the offline/CI path: no conversation id means no context, and an empty fragment
    # leaves the assembled prompt byte-identical to the pre-ADR-023 prompt.
    assert render_context_block() == ""
    assert render_context_block(history=[], thread_summary=None, pinned_slots={}) == ""


def test_render_context_block_carries_all_three_layers():
    block = render_context_block(
        history=[PRIOR_TURN],
        thread_summary="She is shopping for a December wedding.",
        pinned_slots={"budget": "50k"},
    )

    assert "December wedding" in block
    assert "budget: 50k" in block
    assert "Any pinkish gowns?" in block


# ---------------------------------------------------------------------------
# The schema half of the transport
# ---------------------------------------------------------------------------


@pytest.mark.parametrize("state_cls", [MemoryAgentState, VisualAgentState, CommerceAgentState])
@pytest.mark.parametrize("field", CONTEXT_FIELDS)
def test_every_subagent_state_declares_the_context_transport(state_cls, field):
    # LangGraph drops undeclared keys without a warning, so a missing declaration is a context
    # loss that no mocked-sub-graph test can see. This guard is what stops the next subagent from
    # being added without the transport.
    assert field in state_cls.__annotations__


# ---------------------------------------------------------------------------
# The shared prompt assembler
# ---------------------------------------------------------------------------


def test_dialogue_context_is_an_optional_fourth_fragment():
    org = {"plan_tier": "seed"}
    with_context = assemble_system_prompt("memory", org, dialogue_context="Customer: hi")

    assert with_context.endswith("Customer: hi")
    assert assemble_system_prompt("memory", org) in with_context


def test_an_absent_dialogue_context_leaves_the_prompt_byte_identical():
    org = {"plan_tier": "seed"}
    baseline = assemble_system_prompt("memory", org)

    assert assemble_system_prompt("memory", org, dialogue_context=None) == baseline
    assert assemble_system_prompt("memory", org, dialogue_context="") == baseline
    assert assemble_system_prompt("memory") == assemble_system_prompt(
        "memory", None, dialogue_context=None
    )


# ---------------------------------------------------------------------------
# Offline path and budget
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_no_conversation_id_yields_an_empty_dialogue_block():
    # `load_context` short-circuits without a conversation id (the CI/offline path); every
    # rendered block must then be empty, so the deterministic prompt is unchanged.
    state = await run_load_context({"org_context": {}})

    assert render_context_block(
        state["history"], state["thread_summary"], state["pinned_slots"]
    ) == ""


def test_the_rendered_block_adds_no_budget_of_its_own():
    # The renderer is a pure function of the already-bounded window: it adds a fixed prefix per
    # turn and no trimming of its own, so the window's policy threshold remains the only budget.
    budget = 200
    turns = [{"authorKind": "Customer", "text": "x" * 120} for _ in range(20)]
    window = fit_to_budget(turns, budget)

    block = render_context_block(history=window.kept)

    assert len(block) <= estimate_tokens(window.kept) * 4
