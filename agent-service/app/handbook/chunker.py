"""Chunk handbook markdown into retrievable pieces (ADR-025).

The corpus is hand-written GFM with no front matter and no MDX, so chunking is a heading walk
rather than a markup parse. Three rules shape it:

1. **Chunk on ``H2`` boundaries.** Pages are 74-179 lines; a whole page is too coarse to retrieve
   and an ``H3``-only chunk fragments a table away from the heading that gives it meaning.
   ``H3`` sections therefore stay inside their parent ``H2``, except under a heading named in
   ``promote_h3``, where each ``H3`` is a real procedure and deserves its own chunk.
2. **Never split a line block.** Splitting is only ever done at a heading, so a markdown table
   (which carries most of the load-bearing facts in this corpus) always stays whole. Nothing is
   stripped either: inline code such as ``/app/b/<your-boutique>/<section>`` must survive intact,
   because an HTML-stripping pre-cleaner would eat exactly the route facts users ask about.
3. **The page intro is its own chunk, not a prefix on every chunk.** The first draft of the plan
   prepended the ``H1`` and intro paragraph to every chunk. With a *lexical* leg in the retrieval
   (ADR-025, D5) that would be actively harmful: the intro's terms would appear in every chunk and
   match every lexical query equally. The page title and heading trail already reach the lexical
   index with heavier weights through ``HandbookChunk.SearchVector``, which supplies the same
   context signal without duplication.
"""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass, field

_HEADING_RE = re.compile(r"^(#{1,6})\s+(.*?)\s*$")
_FENCE_RE = re.compile(r"^\s*(```|~~~)")
_THEMATIC_BREAK_RE = re.compile(r"^-{3,}\s*$")
_ANCHOR_STRIP_RE = re.compile(r"[^a-z0-9\s-]")
_ANCHOR_SPACE_RE = re.compile(r"[\s_]+")
_BLANK_RUN_RE = re.compile(r"\n{3,}")

#: Heading depths the chunker understands as structure rather than content.
_H1 = 1
_H2 = 2
_H3 = 3


@dataclass(frozen=True)
class HandbookChunkDraft:
    """One chunk, ready to POST to ``/internal/handbook/chunks``."""

    source_key: str
    source_kind: str
    source_title: str
    source_url: str
    heading_path: str
    anchor: str | None
    content: str
    content_hash: str
    audience: str
    ordinal: int
    tags_json: str = "{}"


def slugify(text: str) -> str:
    """A GitHub-style heading anchor: lowercase, punctuation dropped, spaces hyphenated."""
    lowered = text.strip().lower()
    return _ANCHOR_SPACE_RE.sub("-", _ANCHOR_STRIP_RE.sub("", lowered)).strip("-")


def normalise(markdown: str) -> str:
    """Normalise line endings and blank runs so the content hash is stable across checkouts."""
    text = markdown.replace("\r\n", "\n").replace("\r", "\n")
    text = "\n".join(line.rstrip() for line in text.split("\n"))
    text = _BLANK_RUN_RE.sub("\n\n", text)
    return text.strip()


def content_hash(content: str) -> str:
    """sha256 of the normalised content: the seeder's change detector."""
    return hashlib.sha256(content.encode("utf-8")).hexdigest()


@dataclass
class _Section:
    level: int
    title: str
    lines: list[str] = field(default_factory=list)
    children: list[_Section] = field(default_factory=list)

    @property
    def body(self) -> str:
        # A standalone `---` is a structural separator between the page intro and its body, not
        # content; keeping it would put three dashes at the head of every intro chunk.
        return normalise(
            "\n".join(line for line in self.lines if not _THEMATIC_BREAK_RE.match(line))
        )


@dataclass(frozen=True)
class _Piece:
    level: int
    title: str
    heading_path: str
    anchor: str
    content: str


def chunk_markdown(
    markdown: str,
    *,
    source_key: str,
    source_kind: str,
    source_title: str,
    source_url: str,
    audience: str = "staff",
    promote_h3: frozenset[str] | set[str] = frozenset(),
    min_chunk_chars: int = 0,
) -> list[HandbookChunkDraft]:
    """Split one markdown source into handbook chunks.

    Args:
        markdown: The raw source. Nothing is stripped, so inline code and tables survive.
        source_key: Stable id, e.g. ``web-docs/salon``.
        source_kind: ``web-docs`` | ``company`` | ``legal``.
        source_title: The page title, used when the document has no ``H1`` of its own.
        source_url: Where a reader can read more, e.g. ``/docs/salon``.
        audience: ``staff`` | ``customer`` | ``both``. Applied to every chunk.
        promote_h3: ``H2`` headings whose ``H3`` children become their own chunks.
        min_chunk_chars: When > 0, consecutive short ``H2`` chunks are merged so the index does not
            fill with three-line fragments. Off by default; the value is tuned against the golden
            query set (ADR-025, Phase 6) rather than guessed here.

    Returns:
        The chunks in document order, ``ordinal`` counting from 0 within the source.
    """
    text = normalise(markdown)
    roots, preamble = _parse(text)
    title = next((s.title for s in roots if s.level == _H1), "") or source_title

    pieces = _pieces(roots, preamble, title, set(promote_h3))
    pieces = _merge_short(pieces, min_chunk_chars)

    return [
        HandbookChunkDraft(
            source_key=source_key,
            source_kind=source_kind,
            source_title=source_title or title,
            source_url=source_url,
            heading_path=piece.heading_path,
            anchor=piece.anchor or None,
            content=piece.content,
            content_hash=content_hash(piece.content),
            audience=audience,
            ordinal=ordinal,
        )
        for ordinal, piece in enumerate(pieces)
        if piece.content
    ]


def _parse(text: str) -> tuple[list[_Section], str]:
    """Build the heading tree, ignoring ``#`` lines inside fenced code blocks."""
    roots: list[_Section] = []
    stack: list[_Section] = []
    preamble: list[str] = []
    in_fence = False

    for line in text.split("\n"):
        if _FENCE_RE.match(line):
            in_fence = not in_fence

        match = None if in_fence else _HEADING_RE.match(line)
        if match is None:
            (stack[-1].lines if stack else preamble).append(line)
            continue

        level = len(match.group(1))
        section = _Section(level=level, title=match.group(2).strip())

        while stack and stack[-1].level >= level:
            stack.pop()

        (stack[-1].children if stack else roots).append(section)
        stack.append(section)

    return roots, normalise(
        "\n".join(line for line in preamble if not _THEMATIC_BREAK_RE.match(line))
    )


def _pieces(
    roots: list[_Section],
    preamble: str,
    title: str,
    promote_h3: set[str],
) -> list[_Piece]:
    """Turn the heading tree into ordered pieces: an intro piece, then one per ``H2``."""
    h1 = next((s for s in roots if s.level == _H1), None)

    if h1 is None:
        # No headings at all: the document is one chunk, titled by its metadata.
        body = normalise("\n".join(filter(None, [preamble, normalise(_text_of(roots))])))
        return [_Piece(_H2, title, title, slugify(title), body)] if body else []

    intro_parts = [preamble, h1.body]
    intro = normalise("\n".join(part for part in intro_parts if part))

    pieces: list[_Piece] = []
    if intro:
        pieces.append(_Piece(_H1, title, title, slugify(title), intro))

    for h2 in h1.children:
        if h2.level != _H2:
            continue

        if h2.title in promote_h3 and h2.children:
            # A procedure-per-H3 section: the H2 lead-in (if any) is its own piece, and each H3 is
            # a piece too, so no procedure inherits a neighbour's steps.
            if h2.body:
                pieces.append(_Piece(
                    _H2, h2.title, f"{title} › {h2.title}", slugify(h2.title), h2.body))
            for h3 in h2.children:
                if h3.level == _H3 and h3.body:
                    pieces.append(_Piece(
                        _H3,
                        h3.title,
                        f"{title} › {h2.title} › {h3.title}",
                        slugify(h3.title),
                        h3.body,
                    ))
            continue

        body = _with_nested_headings(h2)
        if body:
            pieces.append(_Piece(
                _H2, h2.title, f"{title} › {h2.title}", slugify(h2.title), body))

    return pieces


def _with_nested_headings(section: _Section) -> str:
    """The section body with its ``H3`` children folded back in, headings preserved."""
    parts = [section.body]
    for child in section.children:
        if child.level == _H3 and child.body:
            parts.append(f"### {child.title}\n\n{child.body}")
    return normalise("\n\n".join(part for part in parts if part))


def _text_of(roots: list[_Section]) -> str:
    return "\n\n".join(section.body for section in roots if section.body)


def _merge_short(pieces: list[_Piece], min_chunk_chars: int) -> list[_Piece]:
    """Merge consecutive short ``H2`` pieces so the index has no three-line fragments."""
    if min_chunk_chars <= 0:
        return pieces

    merged: list[_Piece] = []
    for piece in pieces:
        previous = merged[-1] if merged else None
        if (
            previous is not None
            and piece.level == _H2
            and previous.level == _H2
            and len(previous.content) < min_chunk_chars
        ):
            merged[-1] = _Piece(
                _H2,
                previous.title,
                previous.heading_path,
                previous.anchor,
                normalise(f"{previous.content}\n\n## {piece.title}\n\n{piece.content}"),
            )
        else:
            merged.append(piece)

    return merged
