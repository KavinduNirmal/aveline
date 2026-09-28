"""Evaluation tests for customer-memory retrieval (ADR-017, adopting ADR-025).

The scoring is pure and the searcher is injected, so the harness is testable without a database.
What these tests cannot settle is recall itself - that needs a seeded store and the live index,
which is what `memory/golden-queries.json` exists for.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from app.agents.customer_memory.eval import (
    DEFAULT_MODES,
    by_kind,
    evaluate,
    load_golden_queries,
    rank_of,
)

REPO_ROOT = Path(__file__).resolve().parents[2]
GOLDEN_PATH = REPO_ROOT / "memory" / "golden-queries.json"


def test_the_golden_set_parses_and_is_substantial():
    queries = load_golden_queries(GOLDEN_PATH)

    assert len(queries) >= 10
    assert all(query.question and query.expect for query in queries)


def test_every_expectation_is_satisfied_by_a_seeded_note():
    """The analogue of the handbook's "expected source is one the manifest indexes".

    A golden entry that names a note nobody seeds can never be retrieved, so it would score every
    mode as a miss and read as a retrieval failure rather than as a broken fixture.
    """
    payload = json.loads(GOLDEN_PATH.read_text(encoding="utf-8"))
    seeded = " || ".join(payload["notes_to_seed"]).lower()

    for query in load_golden_queries(GOLDEN_PATH):
        assert query.expect.lower() in seeded, f"{query.expect!r} is not in any seeded note"


def test_the_golden_set_covers_both_query_shapes():
    queries = load_golden_queries(GOLDEN_PATH)

    # A set with only one shape cannot show what fusion buys: the lexical leg carries exact tokens,
    # the dense leg carries paraphrase.
    assert by_kind(queries, "exact")
    assert by_kind(queries, "paraphrase")


def test_a_malformed_entry_is_rejected_rather_than_scored(tmp_path):
    path = tmp_path / "golden.json"
    path.write_text(json.dumps({"queries": [{"question": "q", "expect": "e", "kind": "vibes"}]}))

    with pytest.raises(ValueError, match="kind"):
        load_golden_queries(path)


def test_an_entry_missing_its_expectation_is_rejected(tmp_path):
    path = tmp_path / "golden.json"
    path.write_text(json.dumps({"queries": [{"question": "q", "kind": "exact"}]}))

    with pytest.raises(ValueError, match="question and an expect"):
        load_golden_queries(path)


def test_rank_of_matches_on_containment_not_equality():
    # A note is a sentence; a golden entry names the part of it that must be found.
    assert rank_of([{"content": "Kavindu Nirmal prefers cotton"}], "prefers cotton") == 1


def test_rank_of_ignores_case_and_trailing_punctuation():
    assert rank_of([{"content": "He prefers green tea over coffee."}], "green tea over coffee") == 1


def test_rank_of_returns_none_when_the_note_is_absent():
    assert rank_of([{"content": "prefers linen"}], "prefers cotton") is None


@pytest.mark.asyncio
async def test_evaluate_scores_each_mode_separately():
    queries = load_golden_queries(GOLDEN_PATH)

    async def searcher(question: str, mode: str, top_k: int) -> list[dict]:
        # Hybrid is right on the first hit; the lexical leg misses one query into position 2; the
        # vector leg misses one entirely.
        expected = next(query.expect for query in queries if query.question == question)
        if mode == "hybrid":
            return [{"content": expected}]
        if mode == "lexical" and question == queries[0].question:
            return [{"content": "something else"}, {"content": expected}]
        if mode == "vector" and question == queries[0].question:
            return [{"content": "something else"}]
        return [{"content": expected}]

    reports = {report.mode: report for report in await evaluate(searcher, queries)}

    assert reports["hybrid"].recall_at_1 == 1.0
    assert reports["lexical"].recall_at_1 < 1.0
    assert reports["lexical"].recall_at_3 == 1.0
    assert reports["vector"].recall_at_3 < 1.0
    assert len(reports["vector"].misses) == 1


@pytest.mark.asyncio
async def test_evaluate_reports_an_empty_set_without_dividing_by_zero():
    async def empty() -> list[dict]:
        return []

    reports = await evaluate(lambda *_: empty(), [])

    assert reports[0].recall_at_1 == 0.0
    assert reports[0].total == 0


@pytest.mark.asyncio
async def test_an_empty_response_is_reported_apart_from_a_ranking_miss():
    """An empty dense result usually means the provider hiccuped, not that the index failed."""
    queries = load_golden_queries(GOLDEN_PATH)[:2]

    async def searcher(question: str, mode: str, top_k: int) -> list[dict]:
        return []

    reports = {report.mode: report for report in await evaluate(searcher, queries)}

    for mode in DEFAULT_MODES:
        assert len(reports[mode].empty) == len(queries)
        assert len(reports[mode].misses) == len(queries)
