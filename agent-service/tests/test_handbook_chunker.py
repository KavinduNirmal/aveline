"""Tests for the handbook chunker (ADR-025).

The chunker is the only place content shape is decided, so its failure modes are structural: a
split table, a stripped placeholder or an unstable hash all produce an index that silently answers
worse rather than failing. The rules are pinned here, and once against the real corpus.
"""

from pathlib import Path

import pytest

from app.handbook import chunk_markdown, content_hash, normalise, slugify

DOC = """# Salon

The Salon is where the boutique talks to its clients.

---

## The two panes

The list and the thread.

| Pane | What it shows |
|---|---|
| List | Conversations |
| Thread | Messages |

## Talking to Aveline

Ask her anything.

### The agents you will meet

Aveline, Ava, Elle and Lina.
"""


def _chunks(markdown: str = DOC, **overrides):
    options = {
        "source_key": "web-docs/salon",
        "source_kind": "web-docs",
        "source_title": "Salon",
        "source_url": "/docs/salon",
    }
    options.update(overrides)
    return chunk_markdown(markdown, **options)


# --------------------------------------------------------------------------- shape


def test_emits_an_intro_chunk_then_one_per_h2():
    chunks = _chunks()

    assert [c.heading_path for c in chunks] == [
        "Salon",
        "Salon › The two panes",
        "Salon › Talking to Aveline",
    ]


def test_the_intro_is_its_own_chunk_not_a_prefix_on_every_chunk():
    # Deliberate deviation from the first draft of the plan: with a lexical retrieval leg,
    # duplicating the intro into every chunk would make the intro's terms match every query.
    chunks = _chunks()

    assert chunks[0].content == "The Salon is where the boutique talks to its clients."
    for chunk in chunks[1:]:
        assert "where the boutique talks" not in chunk.content


def test_an_h3_stays_inside_its_parent_h2():
    chunks = _chunks()

    talking = next(c for c in chunks if c.heading_path == "Salon › Talking to Aveline")
    assert "Ask her anything." in talking.content
    assert "### The agents you will meet" in talking.content
    assert "Aveline, Ava, Elle and Lina." in talking.content
    assert not any("agents you will meet" in c.heading_path for c in chunks)


def test_a_promoted_h3_becomes_its_own_chunk():
    chunks = _chunks(promote_h3={"Talking to Aveline"})

    paths = [c.heading_path for c in chunks]
    assert "Salon › Talking to Aveline" in paths
    assert "Salon › Talking to Aveline › The agents you will meet" in paths

    promoted = next(c for c in chunks if c.heading_path.endswith("The agents you will meet"))
    assert promoted.content == "Aveline, Ava, Elle and Lina."


def test_ordinals_are_sequential_from_zero():
    chunks = _chunks()

    assert [c.ordinal for c in chunks] == list(range(len(chunks)))


def test_anchor_is_the_slug_of_the_last_heading():
    chunks = _chunks()

    assert chunks[0].anchor == "salon"
    assert chunks[1].anchor == "the-two-panes"


def test_a_document_without_headings_is_a_single_chunk():
    chunks = _chunks("Just some prose with no headings at all.")

    assert len(chunks) == 1
    assert chunks[0].content == "Just some prose with no headings at all."
    assert chunks[0].heading_path == "Salon"


# --------------------------------------------------------------------------- integrity


def test_a_table_is_never_split_across_chunks():
    chunks = _chunks()

    table_lines = [line for line in DOC.splitlines() if line.startswith("|")]
    for line in table_lines:
        assert any(line in chunk.content for chunk in chunks), line

    # And the table lives in exactly one chunk.
    holders = [c for c in chunks if "| Pane | What it shows |" in c.content]
    assert len(holders) == 1
    assert all(line in holders[0].content for line in table_lines)


def test_inline_code_placeholders_survive_intact():
    # An HTML-stripping pre-cleaner would eat `<your-boutique>` and corrupt every route fact.
    markdown = "# Overview\n\nOpen `/app/b/<your-boutique>/<section>` to get there.\n"

    chunks = _chunks(markdown)

    assert "/app/b/<your-boutique>/<section>" in chunks[0].content


def test_a_heading_inside_a_code_fence_is_not_a_heading():
    markdown = "# Integrations\n\n## Point Meta at the webhook\n\n```\n# not a heading\n```\n"

    chunks = _chunks(markdown)

    assert [c.heading_path for c in chunks] == ["Integrations › Point Meta at the webhook"]
    assert "# not a heading" in chunks[0].content


def test_a_thematic_break_is_not_content():
    chunks = _chunks()

    # A standalone `---`, not the `|---|` separator inside a table.
    assert not any(
        line.strip() == "---" for chunk in chunks for line in chunk.content.splitlines()
    )


# --------------------------------------------------------------------------- hashing


def test_the_hash_is_stable_across_whitespace_and_line_ending_differences():
    a = content_hash(normalise("Line one.  \r\n\r\n\r\nLine two.\r\n"))
    b = content_hash(normalise("Line one.\n\nLine two.\n"))

    assert a == b


def test_the_hash_changes_when_the_content_changes():
    assert content_hash("one") != content_hash("two")


def test_equivalent_documents_produce_equivalent_hashes():
    first = _chunks(DOC.replace("\n", "\r\n"))
    second = _chunks(DOC)

    assert [c.content_hash for c in first] == [c.content_hash for c in second]


# --------------------------------------------------------------------------- merging


def test_short_h2_chunks_are_merged_when_asked():
    markdown = f"# Page\n\n## One\n\nshort.\n\n## Two\n\n{'x' * 200}\n"

    chunks = _chunks(markdown, min_chunk_chars=100)

    # "One" is below the floor, so it is absorbed into its neighbour rather than standing alone as
    # a three-line fragment. The merged chunk keeps its first heading as its path and the absorbed
    # heading in the body, so both titles remain legible and both reach the lexical index.
    assert len(chunks) == 1
    assert chunks[0].heading_path == "Page › One"
    assert "## Two" in chunks[0].content
    assert "short." in chunks[0].content


def test_a_section_at_or_above_the_floor_does_not_absorb_its_neighbour():
    markdown = f"# Page\n\n## One\n\n{'x' * 200}\n\n## Two\n\nshort.\n"

    chunks = _chunks(markdown, min_chunk_chars=100)

    assert len(chunks) == 2
    assert chunks[1].heading_path == "Page › Two"


def test_merging_is_off_by_default():
    markdown = "# Page\n\n## One\n\nshort.\n\n## Two\n\nalso short.\n"

    assert len(_chunks(markdown)) == 2


# --------------------------------------------------------------------------- slugify


@pytest.mark.parametrize(
    ("title", "expected"),
    [
        ("The two panes", "the-two-panes"),
        ("Plan & Billing", "plan-billing"),
        ("Roles & Permissions", "roles-permissions"),
        ("1. Who we are", "1-who-we-are"),
    ],
)
def test_slugify(title, expected):
    assert slugify(title) == expected


# --------------------------------------------------------------------------- the real corpus


def _docs_dir() -> Path:
    return Path(__file__).resolve().parents[2] / "frontend" / "web" / "src" / "docs"


@pytest.mark.skipif(not _docs_dir().is_dir(), reason="frontend docs are not checked out")
def test_every_table_row_in_the_real_corpus_survives_chunking():
    """The corpus carries its load-bearing facts in tables; not one row may be dropped."""
    pages = sorted(_docs_dir().glob("*.md"))
    assert pages, "expected the documentation corpus to be present"

    for page in pages:
        markdown = page.read_text(encoding="utf-8")
        chunks = chunk_markdown(
            markdown,
            source_key=f"web-docs/{page.stem}",
            source_kind="web-docs",
            source_title=page.stem,
            source_url=f"/docs/{page.stem}",
        )

        assert chunks, f"{page.name} produced no chunks"

        table_lines = [line for line in markdown.splitlines() if line.strip().startswith("|")]
        for line in table_lines:
            stripped = line.rstrip()
            assert any(stripped in chunk.content for chunk in chunks), (
                f"{page.name}: table row dropped: {stripped!r}"
            )

        # Every chunk must be addressable and unique within its source.
        assert len({c.ordinal for c in chunks}) == len(chunks)
        assert all(c.content.strip() for c in chunks)
