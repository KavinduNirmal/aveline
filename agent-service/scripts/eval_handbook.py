#!/usr/bin/env python
"""Score the handbook index against the golden query set (ADR-025).

Reports recall@1 and recall@3 for the hybrid, lexical and vector modes separately, for the exact and
paraphrase query shapes and for the set as a whole. This is the only thing that can substantiate "the
handbook retrieves well": unit tests prove the fusion arithmetic and the filters, not the ranking.

``--out PATH`` writes the same numbers as a JSON artefact (timestamp, query count, top-k, audience,
one entry per kind and mode with recall@1/recall@3, plus a per-mode summary over the whole set), so a
figure can be tracked across runs instead of living only in the terminal scrollback. Without
``--out`` the script prints exactly what it always printed and writes nothing.

Examples:

    python scripts/eval_handbook.py --token "$INTERNAL_API_TOKEN"
    python scripts/eval_handbook.py --token "$INTERNAL_API_TOKEN" --mode hybrid --show-misses 5
    python scripts/eval_handbook.py --token "$INTERNAL_API_TOKEN" --out reports/handbook-eval.json
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

_SERVICE_ROOT = Path(__file__).resolve().parents[1]
if str(_SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(_SERVICE_ROOT))

import httpx  # noqa: E402

from app.core.security import INTERNAL_TOKEN_HEADER  # noqa: E402
from app.handbook.eval import (  # noqa: E402
    DEFAULT_MODES,
    ModeReport,
    by_kind,
    evaluate,
    load_golden_queries,
)

_REPO_ROOT = _SERVICE_ROOT.parent
_SEARCH_PATH = "/internal/handbook/search"

#: The key the "all queries" row is filed under, beside the real query kinds.
_ALL_KIND = "all"



def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument(
        "--base-url",
        default=os.environ.get("API_BASE_URL", "http://localhost:5000"),
        help="Base URL of the .NET API (default: $API_BASE_URL).",
    )
    parser.add_argument(
        "--token",
        default=os.environ.get("INTERNAL_API_TOKEN", ""),
        help="Internal service token (default: $INTERNAL_API_TOKEN).",
    )
    parser.add_argument(
        "--queries",
        default=str(_REPO_ROOT / "handbook" / "golden-queries.json"),
        help="The golden query set.",
    )
    parser.add_argument(
        "--mode",
        action="append",
        choices=list(DEFAULT_MODES),
        help="Score only this mode. Repeatable; default is all three.",
    )
    parser.add_argument("--top-k", type=int, default=5, help="Results fetched per query.")
    parser.add_argument("--audience", default="staff", help="Audience filter.")
    parser.add_argument(
        "--show-misses", type=int, default=3, help="How many misses to print per mode (0 for none)."
    )
    parser.add_argument(
        "--out",
        default=None,
        help=(
            "Write the scores as JSON to this path. Without it the script only prints, which keeps "
            "the interactive output unchanged."
        ),
    )
    return parser.parse_args(argv)


def build_report(
    *,
    args: argparse.Namespace,
    queries: list,
    collected: list[tuple[str, list[ModeReport]]],
    generated_at: str | None = None,
) -> dict[str, Any]:
    """Shape the scored rows into the tracked artefact.

    ``collected`` is one ``(kind, reports)`` pair per printed block, in print order: one block per
    query kind and a final ``all`` block over the whole set. The JSON therefore carries the same
    breakdown the terminal shows - mode, kind, recall@1, recall@3 and the query count - plus a
    per-mode summary taken from the ``all`` block.
    """
    results: list[dict[str, Any]] = []
    for kind, reports in collected:
        for report in reports:
            results.append(
                {
                    "kind": kind,
                    "mode": report.mode,
                    "queryCount": report.total,
                    "recall@1": round(report.recall_at_1, 4),
                    "recall@3": round(report.recall_at_3, 4),
                }
            )

    overall = collected[-1][1] if collected else []
    per_mode_summary = {
        report.mode: {
            "queryCount": report.total,
            "recall@1": round(report.recall_at_1, 4),
            "recall@3": round(report.recall_at_3, 4),
        }
        for report in overall
    }

    return {
        "generatedAt": generated_at or datetime.now(UTC).isoformat(timespec="seconds"),
        "baseUrl": args.base_url,
        "topK": args.top_k,
        "audience": args.audience,
        "modes": [report.mode for report in overall],
        "queryCount": len(queries),
        "results": results,
        "perModeSummary": per_mode_summary,
    }


def write_report(path: Path, report: dict[str, Any]) -> None:
    """Write ``report`` as pretty JSON, creating the parent directory when it is missing."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


async def _run(args: argparse.Namespace) -> int:
    queries = load_golden_queries(Path(args.queries))
    if not queries:
        print("The golden set is empty; nothing to score.", file=sys.stderr)
        return 1
    if not args.token:
        print("A token is required: set INTERNAL_API_TOKEN or pass --token.", file=sys.stderr)
        return 2

    modes = tuple(args.mode) if args.mode else DEFAULT_MODES

    #: One ``(kind, reports)`` block per printed section, in print order. Kept so ``--out`` writes
    #: exactly the breakdown that was shown rather than re-running the scoring.
    collected: list[tuple[str, list[ModeReport]]] = []

    async with httpx.AsyncClient(
        base_url=args.base_url.rstrip("/"),
        headers={INTERNAL_TOKEN_HEADER: args.token},
        timeout=60.0,
    ) as client:

        async def searcher(question: str, mode: str, top_k: int) -> list[dict]:
            response = await client.post(
                _SEARCH_PATH,
                json={
                    "query": question,
                    "mode": mode,
                    "audience": args.audience,
                    "topK": top_k,
                },
            )
            response.raise_for_status()
            payload = response.json()
            return payload if isinstance(payload, list) else []

        print(f"Scoring {len(queries)} golden queries at top-{args.top_k}.\n")

        for kind in sorted({query.kind for query in queries}):
            subset = by_kind(queries, kind)
            print(f"{kind} ({len(subset)} queries)")
            reports = await evaluate(searcher, subset, modes=modes, top_k=args.top_k)
            for report in reports:
                print(f"  {report.line()}")
                for miss in report.misses[: args.show_misses]:
                    print(f"      miss: {miss}")
            print()
            collected.append((kind, reports))

        print("all queries")
        overall = await evaluate(searcher, queries, modes=modes, top_k=args.top_k)
        for report in overall:
            print(f"  {report.line()}")
        collected.append((_ALL_KIND, overall))

    if args.out:
        report = build_report(args=args, queries=queries, collected=collected)
        write_report(Path(args.out), report)
        print(f"\nWrote {args.out}")

    return 0


def main(argv: list[str] | None = None) -> int:
    return asyncio.run(_run(_parse_args(argv)))


if __name__ == "__main__":
    raise SystemExit(main())
