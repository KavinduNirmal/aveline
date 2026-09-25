#!/usr/bin/env python
"""Seed the handbook knowledge base from a repository checkout (ADR-025).

Reads the product documentation in place plus the authored company pages, chunks them, and pushes
each chunk to the `.NET` API's internal handbook endpoint, which embeds and upserts it. Seeding is
idempotent: re-running against unchanged sources writes nothing.

Examples:

    # See what would be uploaded, making no HTTP calls at all.
    python scripts/seed_handbook.py --dry-run

    # Seed a local API.
    python scripts/seed_handbook.py --base-url http://localhost:5000 --token "$INTERNAL_API_TOKEN"

    # Re-seed one source and retire any source no longer in the manifest.
    python scripts/seed_handbook.py --source company/faq --prune
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

from app.handbook.seeder import HandbookSeeder  # noqa: E402
from app.handbook.sources import discover, load_chunks  # noqa: E402

_REPO_ROOT = _SERVICE_ROOT.parent


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--base-url",
        default=os.environ.get("API_BASE_URL", "http://localhost:5000"),
        help="Base URL of the .NET API (default: $API_BASE_URL or http://localhost:5000).",
    )
    parser.add_argument(
        "--token",
        default=os.environ.get("INTERNAL_API_TOKEN", ""),
        help="Internal service token (default: $INTERNAL_API_TOKEN).",
    )
    parser.add_argument(
        "--source",
        action="append",
        default=[],
        help="Seed only this source key, e.g. web-docs/salon. Repeatable.",
    )
    parser.add_argument(
        "--prune",
        action="store_true",
        help="Delete indexed sources that are no longer in the manifest.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Report what would be uploaded without making any HTTP call.",
    )
    parser.add_argument(
        "--support-email",
        default=os.environ.get("SUPPORT_EMAIL", ""),
        help="Value substituted for {{SUPPORT_EMAIL}} (default: $SUPPORT_EMAIL).",
    )
    parser.add_argument(
        "--docs-dir",
        default=str(_REPO_ROOT / "frontend" / "web" / "src" / "docs"),
        help="The product documentation corpus.",
    )
    parser.add_argument(
        "--company-dir",
        default=str(_REPO_ROOT / "handbook" / "company"),
        help="The authored company handbook pages.",
    )
    parser.add_argument(
        "--min-chunk-chars",
        type=int,
        default=0,
        help="Merge consecutive H2 chunks shorter than this. 0 disables merging (the default).",
    )
    return parser.parse_args(argv)


async def _run(args: argparse.Namespace) -> int:
    if not args.token and not args.dry_run:
        print("A token is required: set INTERNAL_API_TOKEN or pass --token.", file=sys.stderr)
        return 2

    sources = discover(
        docs_dir=Path(args.docs_dir),
        company_dir=Path(args.company_dir),
        only=set(args.source) or None,
    )
    if not sources:
        print("No sources matched; nothing to seed.", file=sys.stderr)
        return 1

    drafts = load_chunks(
        sources,
        support_email=args.support_email or None,
        min_chunk_chars=args.min_chunk_chars,
    )
    print(f"Discovered {len(sources)} source(s), chunked into {len(drafts)} chunk(s).")

    async with HandbookSeeder(args.base_url, args.token) as seeder:
        result = await seeder.seed(drafts, prune=args.prune, dry_run=args.dry_run)

    print(result.summary())
    return 0


def main(argv: list[str] | None = None) -> int:
    return asyncio.run(_run(_parse_args(argv)))


if __name__ == "__main__":
    raise SystemExit(main())
