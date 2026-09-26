"""The handbook corpus manifest: which files are indexed, under which identity (ADR-025).

Two corpora feed the index, and they are deliberately different in how they are discovered:

* **``frontend/web/src/docs/*.md``** is the public product documentation and the source of truth
  for it. It is read *in place* rather than copied, so a docs edit can never drift from the index
  that describes it. The title comes from each page's own ``H1`` (the corpus inventory verified all
  fifteen match ``config.ts``), and the URL is derived from the file name.
* **``handbook/company/*.md``** is Aveline's own company knowledge: support, plans, Blossoms, the
  legal terms and an explicit "what this does not cover" page. It is listed explicitly here rather
  than discovered, because these pages need metadata a file name cannot carry (the URL a reader
  should be sent to, and the audience).

Discovery is separated from chunking so both are unit-testable without a network or a database.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from app.handbook.chunker import HandbookChunkDraft, chunk_markdown

#: Pages whose ``H3`` sections are real procedures and deserve their own chunk. Everything else
#: keeps its ``H3``s nested, so a table never loses the heading that explains it.
PROMOTE_H3: dict[str, frozenset[str]] = {
    "web-docs/catalog": frozenset({"Pieces"}),
}

#: The placeholder the seeder substitutes before hashing, so the stored hash reflects what is
#: actually indexed. The support address is expected to change in production (OQ1); a placeholder
#: makes that a re-seed with a different value rather than a content edit.
SUPPORT_EMAIL_PLACEHOLDER = "{{SUPPORT_EMAIL}}"


@dataclass(frozen=True)
class CompanyPage:
    """One authored company page, with the metadata its file name cannot carry."""

    key: str
    filename: str
    title: str
    url: str
    audience: str = "staff"


@dataclass(frozen=True)
class HandbookSource:
    """A resolved, indexable source."""

    key: str
    kind: str
    title: str
    url: str
    audience: str
    path: Path
    promote_h3: frozenset[str] = field(default_factory=frozenset)


#: The company corpus. Order is the order it is seeded; each page is independent.
COMPANY_PAGES: tuple[CompanyPage, ...] = (
    CompanyPage("company/about-aveline", "about-aveline.md", "About Aveline", "/"),
    CompanyPage(
        "company/plans-and-pricing", "plans-and-pricing.md", "Plans & Pricing", "/docs/billing"
    ),
    CompanyPage("company/blossoms", "blossoms.md", "Blossoms", "/docs/usage"),
    CompanyPage(
        "company/contact-and-support", "contact-and-support.md", "Contact & Support", "/contact"
    ),
    CompanyPage("company/terms-and-privacy", "terms-and-privacy.md", "Terms & Privacy", "/terms"),
    CompanyPage(
        "company/what-the-handbook-does-not-cover",
        "what-the-handbook-does-not-cover.md",
        "What the Handbook Does Not Cover",
        "/contact",
    ),
    CompanyPage("company/faq", "faq.md", "Frequently Asked Questions", "/contact"),
)


def page_title(markdown: str, fallback: str) -> str:
    """The page's own ``H1``, or a readable fallback derived from its file name."""
    for line in markdown.splitlines():
        stripped = line.strip()
        if stripped.startswith("# "):
            return stripped[2:].strip()
        if stripped:
            break
    return fallback.replace("-", " ").title()


def discover_web_docs(docs_dir: Path) -> list[HandbookSource]:
    """Every product documentation page, read in place from the frontend corpus."""
    if not docs_dir.is_dir():
        raise FileNotFoundError(
            f"The product documentation corpus was not found at {docs_dir}. The handbook is seeded "
            "from a repository checkout; run the seeder from one."
        )

    sources: list[HandbookSource] = []
    for path in sorted(docs_dir.glob("*.md")):
        key = f"web-docs/{path.stem}"
        sources.append(
            HandbookSource(
                key=key,
                kind="web-docs",
                title=page_title(path.read_text(encoding="utf-8"), path.stem),
                url=f"/docs/{path.stem}",
                audience="staff",
                path=path,
                promote_h3=PROMOTE_H3.get(key, frozenset()),
            )
        )

    return sources


def discover_company_docs(company_dir: Path) -> list[HandbookSource]:
    """The authored company pages. A listed page that is missing is an error, not a silent skip."""
    sources: list[HandbookSource] = []
    for page in COMPANY_PAGES:
        path = company_dir / page.filename
        if not path.is_file():
            raise FileNotFoundError(
                f"Company handbook page '{page.filename}' is listed in the manifest but missing "
                f"from {company_dir}."
            )
        sources.append(
            HandbookSource(
                key=page.key,
                kind="company",
                title=page.title,
                url=page.url,
                audience=page.audience,
                path=path,
            )
        )

    return sources


def discover(
    *,
    docs_dir: Path,
    company_dir: Path,
    only: set[str] | None = None,
) -> list[HandbookSource]:
    """Both corpora, optionally filtered to a set of source keys."""
    sources = discover_web_docs(docs_dir) + discover_company_docs(company_dir)
    if only:
        sources = [source for source in sources if source.key in only]
    return sources


def load_chunks(
    sources: list[HandbookSource],
    *,
    support_email: str | None = None,
    min_chunk_chars: int = 0,
) -> list[HandbookChunkDraft]:
    """Chunk every source in order, substituting the support address first so the hash matches."""
    drafts: list[HandbookChunkDraft] = []
    for source in sources:
        markdown = source.path.read_text(encoding="utf-8")
        if support_email:
            markdown = markdown.replace(SUPPORT_EMAIL_PLACEHOLDER, support_email)

        drafts.extend(
            chunk_markdown(
                markdown,
                source_key=source.key,
                source_kind=source.kind,
                source_title=source.title,
                source_url=source.url,
                audience=source.audience,
                promote_h3=source.promote_h3,
                min_chunk_chars=min_chunk_chars,
            )
        )

    return drafts
