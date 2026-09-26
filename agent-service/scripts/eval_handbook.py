#!/usr/bin/env python
"""Score the handbook index against the golden query set (ADR-025).

Reports recall@1 and recall@3 for the hybrid, lexical and vector modes separately, for the exact and
paraphrase query shapes and for the set as a whole. This is the only thing that can substantiate "the
handbook retrieves well": unit tests prove the fusion arithmetic and the filters, not the ranking.

Examples:

    python scripts/eval_handbook.py --token "$INTERNAL_API_TOKEN"
    python scripts/eval_handbook.py --token "$INTERNAL_API_TOKEN" --mode hybrid --show-misses 5
"""

from __future__ import annotations

import argparse
import asyncio
import os
import sys
from pathlib import Path

_SERVICE_ROOT = Path(__file__).resolve().parents[1]
if str(_SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(_SERVICE_ROOT))

import httpx  # noqa: E402

from app.core.security import INTERNAL_TOKEN_HEADER  # noqa: E402
from app.handbook.eval import DEFAULT_MODES, by_kind, evaluate, load_golden_queries  # noqa: E402

_REPO_ROOT = _SERVICE_ROOT.parent
_SEARCH_PATH = "/internal/handbook/search"


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
    return parser.parse_args(argv)


async def _run(args: argparse.Namespace) -> int:
    queries = load_golden_queries(Path(args.queries))
    if not queries:
        print("The golden set is empty; nothing to score.", file=sys.stderr)
        return 1
    if not args.token:
        print("A token is required: set INTERNAL_API_TOKEN or pass --token.", file=sys.stderr)
        return 2

    modes = tuple(args.mode) if args.mode else DEFAULT_MODES

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
            for report in await evaluate(searcher, subset, modes=modes, top_k=args.top_k):
                print(f"  {report.line()}")
                for miss in report.misses[: args.show_misses]:
                    print(f"      miss: {miss}")
            print()

        print("all queries")
        for report in await evaluate(searcher, queries, modes=modes, top_k=args.top_k):
            print(f"  {report.line()}")

    return 0


def main(argv: list[str] | None = None) -> int:
    return asyncio.run(_run(_parse_args(argv)))


if __name__ == "__main__":
    raise SystemExit(main())
