"""Golden-query evaluation for handbook retrieval (ADR-025).

Retrieval quality is the one claim unit tests cannot settle: they can prove the fusion arithmetic and
the filters, but not that the index returns the right page for a real question. This module scores a
labeled query set against the live index and reports recall for the dense leg, the lexical leg and
the hybrid **separately**, so a fusion win or loss is visible rather than assumed.

The searcher is injected, so the scoring itself is unit-testable without a database or an API.
"""

from __future__ import annotations

import json
from collections.abc import Awaitable, Callable, Iterable, Sequence
from dataclasses import dataclass, field
from pathlib import Path

#: The modes reported on separately. `hybrid` is what ships; the other two exist so the
#: contribution of each leg can be seen.
DEFAULT_MODES: tuple[str, ...] = ("hybrid", "lexical", "vector")

#: The two query shapes the golden set is tagged with.
QUERY_KINDS: frozenset[str] = frozenset({"exact", "paraphrase"})

#: `(question, mode, top_k) -> hits`, where each hit carries its source key.
Searcher = Callable[[str, str, int], Awaitable[list[dict]]]


@dataclass(frozen=True)
class GoldenQuery:
    """One question and the source page a correct answer must come from."""

    question: str
    expect: str
    kind: str


@dataclass
class ModeReport:
    """Recall for one retrieval mode."""

    mode: str
    total: int
    hits_at_1: int
    hits_at_3: int
    misses: list[str] = field(default_factory=list)
    #: Queries for which the mode returned nothing at all. Kept apart from `misses` because an
    #: empty dense result usually means the embedding provider hiccuped, not that the index failed:
    #: a real retrieval miss has neighbours, and scoring a provider outage as a ranking error
    #: understates the mode without saying so.
    empty: list[str] = field(default_factory=list)

    @property
    def recall_at_1(self) -> float:
        return self.hits_at_1 / self.total if self.total else 0.0

    @property
    def recall_at_3(self) -> float:
        return self.hits_at_3 / self.total if self.total else 0.0

    def line(self) -> str:
        line = (
            f"{self.mode:<9} recall@1 {self.recall_at_1:6.1%}   "
            f"recall@3 {self.recall_at_3:6.1%}   n={self.total}"
        )
        if self.empty:
            line += f"   ({len(self.empty)} returned no rows)"
        return line


def load_golden_queries(path: Path) -> list[GoldenQuery]:
    """Read the golden set, rejecting anything malformed rather than silently scoring less."""
    payload = json.loads(path.read_text(encoding="utf-8"))

    queries: list[GoldenQuery] = []
    for entry in payload.get("queries", []):
        question = str(entry.get("question", "")).strip()
        expect = str(entry.get("expect", "")).strip()
        kind = str(entry.get("kind", "")).strip()

        if not question or not expect:
            raise ValueError(f"A golden query needs a question and an expect: {entry!r}")
        if kind not in QUERY_KINDS:
            raise ValueError(f"{question!r} has kind {kind!r}, not one of {sorted(QUERY_KINDS)}")

        queries.append(GoldenQuery(question=question, expect=expect, kind=kind))

    return queries


async def evaluate(
    searcher: Searcher,
    queries: Sequence[GoldenQuery],
    *,
    modes: Iterable[str] = DEFAULT_MODES,
    top_k: int = 5,
) -> list[ModeReport]:
    """Score ``queries`` in every mode and return one report per mode."""
    reports: list[ModeReport] = []

    for mode in modes:
        hits_at_1 = 0
        hits_at_3 = 0
        misses: list[str] = []
        empty: list[str] = []

        for query in queries:
            hits = await searcher(query.question, mode, top_k)
            rank = rank_of(hits, query.expect)

            if not hits:
                empty.append(query.question)

            if rank == 1:
                hits_at_1 += 1
            if rank is not None and rank <= 3:
                hits_at_3 += 1
            else:
                misses.append(f"{query.question} -> expected {query.expect}")

        reports.append(
            ModeReport(
                mode=mode,
                total=len(queries),
                hits_at_1=hits_at_1,
                hits_at_3=hits_at_3,
                misses=misses,
                empty=empty,
            )
        )

    return reports


def rank_of(hits: Sequence[dict] | None, expected_source_key: str) -> int | None:
    """The 1-based rank of the first hit from the expected source, or None when it is absent."""
    for index, hit in enumerate(hits or [], start=1):
        if not isinstance(hit, dict):
            continue
        key = str(hit.get("sourceKey") or hit.get("source_key") or "")
        if key == expected_source_key:
            return index
    return None


def by_kind(queries: Sequence[GoldenQuery], kind: str) -> list[GoldenQuery]:
    """The subset of ``queries`` tagged with ``kind``, so exact and paraphrase can be read apart."""
    return [query for query in queries if query.kind == kind]
