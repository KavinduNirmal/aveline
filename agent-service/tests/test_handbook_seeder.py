"""Tests for the handbook corpus manifest and the seeder (ADR-025).

The seeder is the only writer of the index, and it runs against a real deployment. So what matters
here is that it sends exactly the right payload to exactly the right place, that a dry run touches
nothing, and that pruning cannot delete a source the manifest still owns.
"""

import json
from pathlib import Path

import httpx
import pytest

from app.handbook.seeder import INTERNAL_TOKEN_HEADER, HandbookSeeder
from app.handbook.sources import (
    COMPANY_PAGES,
    SUPPORT_EMAIL_PLACEHOLDER,
    discover,
    discover_company_docs,
    discover_web_docs,
    load_chunks,
    page_title,
)

REPO_ROOT = Path(__file__).resolve().parents[2]
DOCS_DIR = REPO_ROOT / "frontend" / "web" / "src" / "docs"
COMPANY_DIR = REPO_ROOT / "handbook" / "company"


# --------------------------------------------------------------------------- discovery


def test_every_product_doc_page_is_discovered_with_its_own_title():
    sources = discover_web_docs(DOCS_DIR)

    assert len(sources) == 15
    by_key = {source.key: source for source in sources}

    salon = by_key["web-docs/salon"]
    assert salon.kind == "web-docs"
    assert salon.title == "Salon"
    assert salon.url == "/docs/salon"
    assert salon.audience == "staff"


def test_the_catalog_page_promotes_its_pieces_procedures():
    sources = {source.key: source for source in discover_web_docs(DOCS_DIR)}

    assert sources["web-docs/catalog"].promote_h3 == frozenset({"Pieces"})
    assert sources["web-docs/salon"].promote_h3 == frozenset()


def test_a_missing_docs_corpus_is_an_error_not_an_empty_index(tmp_path):
    with pytest.raises(FileNotFoundError):
        discover_web_docs(tmp_path / "nowhere")


def test_every_manifested_company_page_is_discovered():
    sources = discover_company_docs(COMPANY_DIR)

    assert len(sources) == len(COMPANY_PAGES)
    assert {source.key for source in sources} == {page.key for page in COMPANY_PAGES}
    assert all(source.kind == "company" for source in sources)
    assert all(source.audience == "staff" for source in sources)


def test_a_manifested_company_page_missing_from_disk_fails_loudly(tmp_path):
    with pytest.raises(FileNotFoundError, match="missing"):
        discover_company_docs(tmp_path)


def test_only_restricts_discovery_to_named_sources():
    sources = discover(docs_dir=DOCS_DIR, company_dir=COMPANY_DIR, only={"company/faq"})

    assert [source.key for source in sources] == ["company/faq"]


def test_page_title_prefers_the_documents_own_h1():
    assert page_title("# Salon\n\nBody.\n", "salon") == "Salon"
    assert page_title("Body with no heading.\n", "joining-a-boutique") == "Joining A Boutique"


# --------------------------------------------------------------------------- chunk loading


def test_loading_chunks_restarts_ordinals_for_each_source():
    sources = discover(docs_dir=DOCS_DIR, company_dir=COMPANY_DIR, only={"web-docs/salon", "company/faq"})

    drafts = load_chunks(sources)

    salon = [draft for draft in drafts if draft.source_key == "web-docs/salon"]
    faq = [draft for draft in drafts if draft.source_key == "company/faq"]
    assert [draft.ordinal for draft in salon] == list(range(len(salon)))
    assert [draft.ordinal for draft in faq] == list(range(len(faq)))


def test_the_support_address_is_substituted_before_hashing(tmp_path):
    page = tmp_path / "contact.md"
    page.write_text(
        "# Contact & Support\n\n## Support\n\nEmail {{SUPPORT_EMAIL}} for help.\n", encoding="utf-8"
    )
    company_dir = tmp_path / "company"
    company_dir.mkdir()
    (company_dir / "contact-and-support.md").write_text(
        page.read_text(encoding="utf-8"), encoding="utf-8"
    )

    # Build one source by hand so the test does not depend on the whole manifest.
    from app.handbook.sources import HandbookSource

    source = HandbookSource(
        key="company/contact-and-support",
        kind="company",
        title="Contact & Support",
        url="/contact",
        audience="staff",
        path=company_dir / "contact-and-support.md",
    )

    resolved = load_chunks([source], support_email="support@aveline.test")
    unresolved = load_chunks([source], support_email=None)

    assert SUPPORT_EMAIL_PLACEHOLDER not in resolved[0].content
    assert "support@aveline.test" in resolved[0].content
    # The hash tracks the resolved content, so changing the address re-embeds that chunk.
    assert resolved[0].content_hash != unresolved[0].content_hash


# --------------------------------------------------------------------------- the seeder


class _Recorder:
    """A mock transport that records every request and replies with scripted bodies."""

    def __init__(self, *, sources: list[str] | None = None) -> None:
        self.requests: list[httpx.Request] = []
        self.bodies: list[dict] = []
        self._sources = sources or []

    def handler(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        if request.method == "GET":
            return httpx.Response(
                200,
                json=[
                    {"sourceKey": key, "sourceKind": "company", "sourceTitle": key, "chunkCount": 1,
                     "updatedAt": "2026-01-01T00:00:00Z"}
                    for key in self._sources
                ],
            )
        if request.method == "DELETE":
            return httpx.Response(200, json={"sourceKey": "x", "removed": 3})
        self.bodies.append(json.loads(request.content))
        return httpx.Response(200, json={"id": "00000000-0000-0000-0000-000000000000"})


def _seeder(recorder: _Recorder) -> HandbookSeeder:
    client = httpx.AsyncClient(
        base_url="http://api.test", transport=httpx.MockTransport(recorder.handler)
    )
    return HandbookSeeder("http://api.test", "secret-token", client=client)


def _drafts(source_key: str = "web-docs/salon", count: int = 3):
    from app.handbook.chunker import HandbookChunkDraft

    return [
        HandbookChunkDraft(
            source_key=source_key,
            source_kind="web-docs",
            source_title="Salon",
            source_url="/docs/salon",
            heading_path=f"Salon › Section {index}",
            anchor=f"section-{index}",
            content=f"Body {index}",
            content_hash=f"hash-{index}",
            audience="staff",
            ordinal=index,
        )
        for index in range(count)
    ]


@pytest.mark.asyncio
async def test_seeding_posts_the_contract_the_endpoint_expects():
    recorder = _Recorder()
    async with _seeder(recorder) as seeder:
        result = await seeder.seed(_drafts())

    assert result.uploaded == 3
    assert result.sources == 1
    assert len(recorder.requests) == 3
    assert all(r.headers[INTERNAL_TOKEN_HEADER] == "secret-token" for r in recorder.requests)

    first = recorder.bodies[0]
    assert first["sourceKey"] == "web-docs/salon"
    assert first["sourceKind"] == "web-docs"
    assert first["headingPath"] == "Salon › Section 0"
    assert first["contentHash"] == "hash-0"
    assert first["ordinal"] == 0
    assert first["audience"] == "staff"
    assert all(r.url.path == "/internal/handbook/chunks" for r in recorder.requests)


@pytest.mark.asyncio
async def test_a_dry_run_makes_no_request_at_all():
    recorder = _Recorder()
    async with _seeder(recorder) as seeder:
        result = await seeder.seed(_drafts(), dry_run=True)

    assert recorder.requests == []
    assert result.dry_run is True
    assert result.chunks == 3
    assert "dry run" in result.summary()


@pytest.mark.asyncio
async def test_pruning_removes_only_sources_the_manifest_no_longer_owns():
    recorder = _Recorder(sources=["web-docs/salon", "web-docs/retired-page"])
    async with _seeder(recorder) as seeder:
        result = await seeder.seed(_drafts(), prune=True)

    deletes = [r for r in recorder.requests if r.method == "DELETE"]
    assert len(deletes) == 1
    assert deletes[0].url.path == "/internal/handbook/sources/web-docs/retired-page"
    assert result.pruned == ["web-docs/retired-page"]


@pytest.mark.asyncio
async def test_pruning_is_opt_in():
    recorder = _Recorder(sources=["web-docs/retired-page"])
    async with _seeder(recorder) as seeder:
        result = await seeder.seed(_drafts())

    assert not [r for r in recorder.requests if r.method == "DELETE"]
    assert result.pruned == []


@pytest.mark.asyncio
async def test_a_rejected_upload_surfaces_as_an_error():
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(400, json={"message": "ContentHash is required."})

    client = httpx.AsyncClient(
        base_url="http://api.test", transport=httpx.MockTransport(handler)
    )
    async with HandbookSeeder("http://api.test", "token", client=client) as seeder:
        with pytest.raises(httpx.HTTPStatusError):
            await seeder.seed(_drafts(count=1))
