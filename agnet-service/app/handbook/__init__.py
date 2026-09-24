"""Handbook knowledge-base tooling (ADR-025).

This package holds the ingestion side of the handbook: the chunker that turns markdown sources
into retrievable pieces, and the seeder that pushes them into the index. The retrieval side lives
in the `.NET` API behind ``/internal/handbook`` (ADR-017 Decision 3), reached from Python through
``app.tools.registry.ToolRegistry.search_handbook``.
"""

from app.handbook.chunker import (
    HandbookChunkDraft,
    chunk_markdown,
    content_hash,
    normalise,
    slugify,
)
from app.handbook.seeder import HandbookSeeder, SeedResult
from app.handbook.sources import (
    COMPANY_PAGES,
    PROMOTE_H3,
    SUPPORT_EMAIL_PLACEHOLDER,
    HandbookSource,
    discover,
    discover_company_docs,
    discover_web_docs,
    load_chunks,
    page_title,
)

__all__ = [
    "COMPANY_PAGES",
    "PROMOTE_H3",
    "SUPPORT_EMAIL_PLACEHOLDER",
    "HandbookChunkDraft",
    "HandbookSeeder",
    "HandbookSource",
    "SeedResult",
    "chunk_markdown",
    "content_hash",
    "discover",
    "discover_company_docs",
    "discover_web_docs",
    "load_chunks",
    "normalise",
    "page_title",
    "slugify",
]
