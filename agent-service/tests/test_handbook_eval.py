"""Tests for the handbook golden-query evaluation (ADR-025).

The evaluation scores a live index, so the tests here cover what can be settled without one: that the
golden set is well-formed and cannot rot, that the scoring arithmetic is right, and that the
``--out`` artefact is written correctly. The scored numbers themselves are produced by
``scripts/eval_handbook.py`` against a seeded database; the ``--out`` plumbing is exercised here
against a mocked HTTP transport, so it is covered without a live server.
"""

import importlib.util
import json
from pathlib import Path

import httpx
import pytest
import respx

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
SCRIPTS_DIR = Path(__file__).resolve().parents[1] / "scripts"


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


# --------------------------------------------------------------------------- the --out artefact


def _load_eval_script():
    """Import ``scripts/eval_handbook.py`` by path: ``scripts/`` is deliberately not a package."""
    spec = importlib.util.spec_from_file_location(
        "eval_handbook_under_test", SCRIPTS_DIR / "eval_handbook.py"
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


_SCRIPT_GOLDEN = {
    "queries": [
        {"question": "how do I invite a staff member?", "expect": "web-docs/team", "kind": "exact"},
        {"question": "who can see the client book?", "expect": "web-docs/salon", "kind": "paraphrase"},
    ]
}

_SCRIPT_EXPECTED = {entry["question"]: entry["expect"] for entry in _SCRIPT_GOLDEN["queries"]}

_SCRIPT_SEARCH_URL = "https://eval.test/internal/handbook/search"


def _write_script_golden(tmp_path: Path) -> Path:
    path = tmp_path / "golden.json"
    path.write_text(json.dumps(_SCRIPT_GOLDEN), encoding="utf-8")
    return path


def _search_response(request: httpx.Request) -> httpx.Response:
    """Hybrid finds the expected page first; the single legs always miss."""
    body = json.loads(request.content)
    expected = _SCRIPT_EXPECTED[body["query"]]
    if body["mode"] == "hybrid":
        hits = [{"sourceKey": expected}]
    else:
        hits = [{"sourceKey": "web-docs/somewhere-else"}]
    return httpx.Response(200, json=hits)


@pytest.mark.asyncio
@respx.mock
async def test_out_can_be_passed_to_write_a_tracked_json_report(tmp_path):
    eval_handbook = _load_eval_script()
    respx.post(_SCRIPT_SEARCH_URL).mock(side_effect=_search_response)
    out = tmp_path / "nested" / "handbook-eval.json"

    code = await eval_handbook._run(
        eval_handbook._parse_args(
            [
                "--base-url",
                "https://eval.test",
                "--token",
                "test-token",
                "--queries",
                str(_write_script_golden(tmp_path)),
                "--out",
                str(out),
            ]
        )
    )

    assert code == 0
    assert out.exists(), "the parent directory must be created, not assumed"

    report = json.loads(out.read_text(encoding="utf-8"))
    assert report["generatedAt"]
    assert report["queryCount"] == 2
    assert report["topK"] == 5
    assert report["audience"] == "staff"
    assert report["modes"] == ["hybrid", "lexical", "vector"]

    # The per-mode summary is over the whole set.
    assert report["perModeSummary"]["hybrid"] == {
        "queryCount": 2,
        "recall@1": 1.0,
        "recall@3": 1.0,
    }
    assert report["perModeSummary"]["lexical"]["recall@1"] == 0.0

    # Every printed block is represented, keyed by kind and mode.
    rows = {(row["kind"], row["mode"]): row for row in report["results"]}
    assert rows[("exact", "hybrid")]["recall@1"] == 1.0
    assert rows[("paraphrase", "hybrid")]["recall@3"] == 1.0
    assert rows[("all", "hybrid")]["queryCount"] == 2
    assert rows[("all", "vector")]["recall@3"] == 0.0


@pytest.mark.asyncio
@respx.mock
async def test_without_out_the_script_only_prints(tmp_path, capsys):
    """The interactive behaviour is unchanged when ``--out`` is absent."""
    eval_handbook = _load_eval_script()
    respx.post(_SCRIPT_SEARCH_URL).mock(side_effect=_search_response)
    out = tmp_path / "never-written.json"

    code = await eval_handbook._run(
        eval_handbook._parse_args(
            [
                "--base-url",
                "https://eval.test",
                "--token",
                "test-token",
                "--queries",
                str(_write_script_golden(tmp_path)),
            ]
        )
    )

    assert code == 0
    assert not out.exists()

    printed = capsys.readouterr().out
    assert "Scoring 2 golden queries at top-5." in printed
    assert "exact (1 queries)" in printed
    assert "paraphrase (1 queries)" in printed
    assert "all queries" in printed
    assert "hybrid    recall@1 100.0%" in printed
    assert "Wrote" not in printed
