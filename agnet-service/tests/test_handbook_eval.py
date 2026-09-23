"""Tests for the handbook golden-query evaluation (ADR-025).

The evaluation scores a live index, so the tests here cover what can be settled without one: that the
golden set is well-formed and cannot rot, and that the scoring arithmetic is right. The scored numbers
themselves are produced by ``scripts/eval_handbook.py`` against a seeded database.
"""

import json
from pathlib import Path

import pytest

from app.handbook.eval import (
    QUERY_KINDS,
    by_kind,
    evaluate,
    load_golden_queries,
    rank_of,
)
from app.handbook.sources import discover

REPO_ROOT = Path(__file__).resolve().parents[2]
GOLDEN_PATH = REPO_ROOT / "handbook" / "golden-queries.json"
DOCS_DIR = REPO_ROOT / "frontend" / "web" / "src" / "docs"
COMPANY_DIR = REPO_ROOT / "handbook" / "company"


# --------------------------------------------------------------------------- the golden set


def test_the_golden_set_parses_and_is_substantial():
    queries = load_golden_queries(GOLDEN_PATH)

    assert len(queries) >= 30
    assert {query.kind for query in queries} == QUERY_KINDS


def test_every_expected_source_is_one_the_manifest_actually_indexes():
    # The failure this guards against is a golden set that keeps scoring a page nobody indexes, or
    # that is never updated when a source is renamed.
    indexed = {
        source.key
        for source in discover(docs_dir=DOCS_DIR, company_dir=COMPANY_DIR)
    }
    queries = load_golden_queries(GOLDEN_PATH)

    unknown = sorted({query.expect for query in queries} - indexed)
    assert unknown == [], f"golden queries expect sources the manifest does not index: {unknown}"


def test_the_golden_set_covers_both_query_shapes():
    queries = load_golden_queries(GOLDEN_PATH)

    assert len(by_kind(queries, "exact")) >= 10
    assert len(by_kind(queries, "paraphrase")) >= 10


def test_the_golden_set_is_free_of_questions():
    questions = [query.question for query in load_golden_queries(GOLDEN_PATH)]

    assert len(questions) == len(set(questions))


def test_a_malformed_entry_is_rejected_rather_than_scored(tmp_path):
    path = tmp_path / "golden.json"
    path.write_text(
        json.dumps({"queries": [{"question": "q", "expect": "web-docs/team", "kind": "vibes"}]}),
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match="kind"):
        load_golden_queries(path)


def test_an_entry_missing_its_expectation_is_rejected(tmp_path):
    path = tmp_path / "golden.json"
    path.write_text(
        json.dumps({"queries": [{"question": "q", "kind": "exact"}]}), encoding="utf-8"
    )

    with pytest.raises(ValueError):
        load_golden_queries(path)


# --------------------------------------------------------------------------- scoring


def test_rank_of_finds_the_first_hit_from_the_expected_source():
    hits = [
        {"sourceKey": "web-docs/salon"},
        {"sourceKey": "web-docs/team"},
        {"sourceKey": "web-docs/team"},
    ]

    assert rank_of(hits, "web-docs/team") == 2
    assert rank_of(hits, "web-docs/nope") is None
    assert rank_of(None, "web-docs/team") is None


def test_rank_of_accepts_snake_case_keys():
    assert rank_of([{"source_key": "company/faq"}], "company/faq") == 1


@pytest.mark.asyncio
async def test_evaluate_scores_each_mode_separately():
    queries = load_golden_queries(GOLDEN_PATH)

    async def searcher(question: str, mode: str, top_k: int) -> list[dict]:
        # The hybrid gets everything right on the first hit; the lexical leg misses one query into
        # position 2; the vector leg misses one entirely.
        expected = next(query.expect for query in queries if query.question == question)
        if mode == "hybrid":
            return [{"sourceKey": expected}]
        if mode == "lexical" and question == queries[0].question:
            return [{"sourceKey": "web-docs/somewhere-else"}, {"sourceKey": expected}]
        if mode == "vector" and question == queries[0].question:
            return [{"sourceKey": "web-docs/somewhere-else"}]
        return [{"sourceKey": expected}]

    reports = {report.mode: report for report in await evaluate(searcher, queries)}

    assert reports["hybrid"].recall_at_1 == 1.0
    assert reports["lexical"].recall_at_1 < 1.0
    assert reports["lexical"].recall_at_3 == 1.0
    assert reports["vector"].recall_at_3 < 1.0
    assert len(reports["vector"].misses) == 1


@pytest.mark.asyncio
async def test_evaluate_reports_an_empty_set_without_dividing_by_zero():
    reports = await evaluate(lambda *_: _empty(), [])

    assert reports[0].recall_at_1 == 0.0
    assert reports[0].total == 0


async def _empty() -> list[dict]:
    return []


@pytest.mark.asyncio
async def test_an_empty_response_is_reported_apart_from_a_ranking_miss():
    # Found live: a transient embedding-provider failure returns no rows at all, which reads as a
    # ranking miss unless it is counted separately.
    queries = load_golden_queries(GOLDEN_PATH)[:2]

    async def searcher(question: str, mode: str, top_k: int) -> list[dict]:
        if question == queries[0].question:
            return []
        return [{"sourceKey": queries[1].expect}]

    report = {r.mode: r for r in await evaluate(searcher, queries)}["hybrid"]

    assert report.empty == [queries[0].question]
    assert len(report.misses) == 1
    assert "returned no rows" in report.line()
