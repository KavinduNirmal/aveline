"""The seeding side of the handbook: push chunked sources into the index (ADR-025).

Seeding is an offline, release-time operation, not a request path. The seeder reads markdown from
a repository checkout, chunks it with :mod:`app.handbook.chunker`, and POSTs each chunk to the
``.NET`` API's ``/internal/handbook/chunks`` endpoint, which embeds the content server-side and
upserts it by ``(sourceKey, ordinal)``. Nothing here calls the frontend, and nothing here runs on
the agent's request path.

The HTTP client is injected so the whole seeder is testable against an ``httpx.MockTransport``
without a live API.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import httpx

from app.core.security import INTERNAL_TOKEN_HEADER
from app.handbook.chunker import HandbookChunkDraft

CHUNKS_PATH = "/internal/handbook/chunks"
SOURCES_PATH = "/internal/handbook/sources"


@dataclass
class SeedResult:
    """What a seed run did, for the operator's log line."""

    sources: int
    chunks: int
    uploaded: int
    pruned: list[str] = field(default_factory=list)
    dry_run: bool = False

    def summary(self) -> str:
        if self.dry_run:
            return (
                f"dry run: {self.chunks} chunk(s) from {self.sources} source(s) would be uploaded"
            )

        line = f"uploaded {self.uploaded} chunk(s) from {self.sources} source(s)"
        if self.pruned:
            line += f"; pruned {len(self.pruned)} retired source(s): {', '.join(self.pruned)}"
        return line


class HandbookSeeder:
    """Posts chunk drafts to the handbook index and prunes retired sources."""

    def __init__(
        self,
        base_url: str,
        token: str,
        *,
        client: httpx.AsyncClient | None = None,
        timeout: float = 30.0,
    ) -> None:
        self._client = client or httpx.AsyncClient(
            base_url=base_url.rstrip("/"),
            timeout=timeout,
        )
        self._owns_client = client is None
        # The token rides on the client either way, so an injected client cannot silently seed
        # unauthenticated.
        if INTERNAL_TOKEN_HEADER not in self._client.headers:
            self._client.headers[INTERNAL_TOKEN_HEADER] = token

    async def __aenter__(self) -> HandbookSeeder:
        return self

    async def __aexit__(self, *_: object) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def upsert(self, draft: HandbookChunkDraft) -> None:
        """Upsert one chunk. The server skips the write when the content hash is unchanged."""
        response = await self._client.post(
            CHUNKS_PATH,
            json={
                "sourceKey": draft.source_key,
                "sourceKind": draft.source_kind,
                "sourceTitle": draft.source_title,
                "sourceUrl": draft.source_url,
                "headingPath": draft.heading_path,
                "anchor": draft.anchor,
                "content": draft.content,
                "contentHash": draft.content_hash,
                "audience": draft.audience,
                "ordinal": draft.ordinal,
                "tagsJson": draft.tags_json,
            },
        )
        response.raise_for_status()

    async def list_source_keys(self) -> list[str]:
        """Every source key currently in the index."""
        response = await self._client.get(SOURCES_PATH)
        response.raise_for_status()
        payload = response.json()
        return [str(entry.get("sourceKey", "")) for entry in payload if entry.get("sourceKey")]

    async def delete_source(self, source_key: str) -> int:
        """Remove a source's chunks. Returns how many rows the server deleted."""
        response = await self._client.delete(f"{SOURCES_PATH}/{source_key}")
        response.raise_for_status()
        return int(response.json().get("removed", 0))

    async def seed(
        self,
        drafts: list[HandbookChunkDraft],
        *,
        prune: bool = False,
        dry_run: bool = False,
    ) -> SeedResult:
        """Upload ``drafts``, optionally pruning sources that are no longer in the manifest.

        A dry run performs no HTTP at all, not even a read, so it is safe against production.
        """
        sources = {draft.source_key for draft in drafts}

        if dry_run:
            return SeedResult(
                sources=len(sources), chunks=len(drafts), uploaded=0, dry_run=True
            )

        for draft in drafts:
            await self.upsert(draft)

        pruned: list[str] = []
        if prune:
            for key in await self.list_source_keys():
                if key not in sources:
                    await self.delete_source(key)
                    pruned.append(key)

        return SeedResult(
            sources=len(sources),
            chunks=len(drafts),
            uploaded=len(drafts),
            pruned=sorted(pruned),
        )
