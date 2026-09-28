"""Tests for the bounded transcript window and compaction (ADR-023, W1.4-W1.6).

The window is a context budget, and its failure modes are quiet - an evicted referent produces a
confident wrong answer rather than an exception - so the boundary behaviour is pinned explicitly.
"""

import json

import pytest

from app.context import compact, estimate_tokens, fit_to_budget


def _turn(text: str, author: str = "System") -> dict:
    return {"id": f"m-{abs(hash(text)) % 10**6}", "authorKind": author, "text": text}


class _StubLlm:
    """A chat model double returning a fixed payload from ``ainvoke``."""

    def __init__(self, content: str) -> None:
        self._content = content
        self.prompts: list[str] = []

    async def ainvoke(self, prompt: str) -> object:
        self.prompts.append(prompt)
        return type("_Response", (), {"content": self._content})()


class _ExplodingLlm:
    async def ainvoke(self, prompt: str) -> object:
        raise RuntimeError("provider unavailable")


# ---------------------------------------------------------------------------
# estimate_tokens
# ---------------------------------------------------------------------------


def test_estimate_tokens_is_zero_for_no_turns():
    assert estimate_tokens([]) == 0


def test_estimate_tokens_grows_with_text_length():
    short = estimate_tokens([_turn("hi")])
    long = estimate_tokens([_turn("hi" * 500)])
    assert long > short


def test_estimate_tokens_tolerates_a_missing_or_non_string_text():
    # A turn with no prose (an attachment-only message) still costs its framing, and must not
    # raise: the window is computed before anything validates the turn's shape.
    assert estimate_tokens([{"authorKind": "User"}]) > 0
    assert estimate_tokens([{"text": 12345}]) > 0


# ---------------------------------------------------------------------------
# fit_to_budget
# ---------------------------------------------------------------------------


def test_fit_to_budget_returns_nothing_for_no_turns():
    result = fit_to_budget([], token_budget=100)
    assert result.kept == []
    assert result.dropped == []
    assert result.was_trimmed is False


def test_fit_to_budget_keeps_everything_when_it_fits():
    turns = [_turn("first"), _turn("second"), _turn("third")]
    result = fit_to_budget(turns, token_budget=10_000)

    assert result.kept == turns
    assert result.dropped == []
    assert result.was_trimmed is False


def test_fit_to_budget_keeps_the_newest_turns_and_drops_the_oldest():
    turns = [_turn(f"turn number {i} " + "x" * 40) for i in range(10)]

    result = fit_to_budget(turns, token_budget=60)

    # The tail is what survives: the most recent exchange is what a reference resolves against.
    assert result.kept == turns[len(turns) - len(result.kept):]
    assert result.kept[-1] == turns[-1]
    assert result.dropped == turns[:len(turns) - len(result.kept)]
    assert result.was_trimmed is True


def test_fit_to_budget_preserves_oldest_first_order():
    turns = [_turn(f"t{i} " + "y" * 40) for i in range(8)]
    result = fit_to_budget(turns, token_budget=60)

    kept_ids = [t["id"] for t in result.kept]
    assert kept_ids == [t["id"] for t in turns if t["id"] in set(kept_ids)]
    assert [t["id"] for t in result.kept] == [
        t["id"] for t in sorted(result.kept, key=lambda t: turns.index(t))
    ]


def test_fit_to_budget_never_splits_a_turn():
    # Every kept turn is a whole turn object, so a question can never be kept without its answer
    # being at least considered. The unit here is a turn, not a token.
    turns = [_turn(f"t{i}") for i in range(5)]
    result = fit_to_budget(turns, token_budget=1)

    assert all(t in turns for t in result.kept)
    assert all(t in turns for t in result.dropped)


def test_fit_to_budget_always_keeps_the_newest_turn_even_when_it_alone_exceeds_the_budget():
    # A window that drops the message just received is useless to every consumer of it.
    older = _turn("older")
    huge = _turn("z" * 10_000)
    result = fit_to_budget([older, huge], token_budget=10)

    assert result.kept == [huge]
    assert result.dropped == [older]


def test_fit_to_budget_with_a_zero_budget_still_returns_one_turn():
    result = fit_to_budget([_turn("a"), _turn("b")], token_budget=0)
    assert len(result.kept) == 1


# ---------------------------------------------------------------------------
# compact
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_compact_is_a_no_op_when_the_window_fits():
    turns = [_turn("hello")]
    llm = _StubLlm(json.dumps({"summary": "unused", "pinned": {}}))

    result = await compact(turns, token_budget=10_000, llm=llm)

    assert result.compacted is False
    assert result.kept == turns
    # Compaction is not run speculatively: no model call when nothing overflowed.
    assert llm.prompts == []


@pytest.mark.asyncio
async def test_compact_carries_a_prior_summary_through_an_untouched_window():
    result = await compact(
        [_turn("hello")],
        token_budget=10_000,
        prior_summary="They are shopping for a wedding.",
        prior_pinned={"budget": "50k"},
    )

    assert result.compacted is False
    assert result.thread_summary == "They are shopping for a wedding."
    assert result.pinned_slots == {"budget": "50k"}


@pytest.mark.asyncio
async def test_compact_summarises_what_leaves_the_window():
    turns = [_turn(f"old turn {i} " + "x" * 80) for i in range(6)] + [_turn("the newest question")]
    llm = _StubLlm(
        json.dumps(
            {
                "summary": "They asked about pink gowns.",
                "pinned": {"active_item": "pink gown", "budget": "50k", "event": None},
            }
        )
    )

    result = await compact(turns, token_budget=40, llm=llm)

    assert result.compacted is True
    assert result.kept[-1] == turns[-1]
    assert result.thread_summary == "They asked about pink gowns."
    # A null slot means "not established" and is not stored as an empty slot.
    assert result.pinned_slots == {"active_item": "pink gown", "budget": "50k"}
    assert len(llm.prompts) == 1


@pytest.mark.asyncio
async def test_compact_includes_the_prior_summary_in_the_prompt():
    turns = [_turn(f"old {i} " + "x" * 80) for i in range(6)] + [_turn("now")]
    llm = _StubLlm(json.dumps({"summary": "merged", "pinned": {}}))

    await compact(turns, token_budget=20, llm=llm, prior_summary="Earlier they preferred silk.")

    assert "Earlier they preferred silk." in llm.prompts[0]


@pytest.mark.asyncio
async def test_compact_without_an_llm_still_bounds_the_window():
    # Offline/CI mode: the window is bounded but no summary is invented. Being explicit about the
    # loss is better than a fabricated summary.
    turns = [_turn(f"old {i} " + "x" * 80) for i in range(6)] + [_turn("now")]
    result = await compact(turns, token_budget=20, llm=None)

    assert result.compacted is True
    assert len(result.kept) < len(turns)
    assert result.thread_summary is None


@pytest.mark.asyncio
async def test_compact_survives_a_provider_failure():
    # Compaction must never break a run: the window is still bounded, so the worst case is a run
    # without a summary.
    turns = [_turn(f"old {i} " + "x" * 80) for i in range(6)] + [_turn("now")]

    result = await compact(turns, token_budget=20, llm=_ExplodingLlm())

    assert result.compacted is True
    assert result.kept[-1] == turns[-1]
    assert result.thread_summary is None


@pytest.mark.asyncio
async def test_compact_tolerates_a_code_fenced_json_response():
    turns = [_turn(f"old {i} " + "x" * 80) for i in range(6)] + [_turn("now")]
    llm = _StubLlm('```json\n{"summary": "fenced", "pinned": {"budget": "10k"}}\n```')

    result = await compact(turns, token_budget=20, llm=llm)

    assert result.thread_summary == "fenced"
    assert result.pinned_slots == {"budget": "10k"}


@pytest.mark.asyncio
async def test_compact_keeps_prose_when_the_model_does_not_return_json():
    turns = [_turn(f"old {i} " + "x" * 80) for i in range(6)] + [_turn("now")]
    llm = _StubLlm("They are still deciding between two gowns.")

    result = await compact(turns, token_budget=20, llm=llm)

    # A model that answered in prose still said something useful; discarding it would lose more
    # than it protects.
    assert result.thread_summary == "They are still deciding between two gowns."
    assert result.pinned_slots == {}
