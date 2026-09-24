"""Translate real agent output into Salon content blocks (ADR-016).

The concierge ``AgentResponse.output`` carries structured sub-output per specialist
(e.g. the Customer Memory Agent's ``memory`` dict). These builders map that structure into
the typed content blocks documented in ``docs/architecture/inbox.md`` §5 so the Salon
renders real content rather than placeholder text.

Only content-producing blocks are emitted; a persona that ran but produced nothing stays
silent (its message is simply omitted by the publisher).
"""

from typing import Any

from app.schemas.customer_memory import normalise_memory_content


def _as_dict(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


# --------------------------------------------------------------------------- Ava (memory)


def build_ava_blocks(memory_output: Any) -> list[dict[str, Any]]:
    """Map the Customer Memory Agent's ``memory`` output into content blocks.

    Returns an empty list when no real content was produced (e.g. no customer context was
    available, so the agent skipped personalization).

    Blocks (in order):
      1. ``text`` - the interaction brief for the customer (if present).
      2. ``at_a_glance`` - the notes on file, then anything extracted this turn, as a
         Category/Content table (if any). One table rather than two: they are the same kind of fact
         to a reader, and the store's own rows come first because that is what "on file" means.
      3. ``suggestion`` - the drafted, customer-facing response (if present).
    """
    memory = _as_dict(memory_output)
    if memory.get("status") == "skipped":
        return []

    blocks: list[dict[str, Any]] = []

    brief = memory.get("interaction_brief")
    if brief:
        blocks.append({"type": "text", "text": str(brief)})

    # On file first, then what this turn learned. Duplicates are collapsed: the brief no longer
    # recites them, so a repeated row would otherwise be the only place the repetition showed - and
    # it showed because the store has no write-time de-duplication.
    rows: list[list[str]] = []
    seen: set[str] = set()
    for source in ("memories_on_file", "extracted_memories"):
        for item in memory.get(source) or []:
            if not isinstance(item, dict):
                continue
            content = str(item.get("content") or "").strip()
            if not content:
                continue
            key = normalise_memory_content(content)
            if key in seen:
                continue
            seen.add(key)
            rows.append([str(item.get("category") or "memory"), content])
    if rows:
        blocks.append({"type": "at_a_glance", "columns": ["Category", "Content"], "rows": rows})

    draft = memory.get("draft_response")
    if draft:
        blocks.append({"type": "suggestion", "text": str(draft)})

    return blocks


# ----------------------------------------------------------------------- Elle (visual)


def build_elle_blocks(visual_output: Any) -> list[dict[str, Any]]:
    """Map the Visual Insight Agent's ``visual`` output into content blocks.

    A ``status == "stub"`` output (the pre-Slice 2 placeholder) has no real content and
    yields no blocks. Once the real graph emits items/looks/a suggestion, they are rendered
    as ``piece``, ``look``, and ``suggestion`` blocks respectively.
    """
    visual = _as_dict(visual_output)
    if visual.get("status") == "stub":
        return []

    blocks: list[dict[str, Any]] = []

    text = visual.get("text") or visual.get("summary")
    if text:
        blocks.append({"type": "text", "text": str(text)})

    suggestion = visual.get("suggestion")
    if suggestion:
        blocks.append({"type": "suggestion", "text": str(suggestion)})

    for item in visual.get("items") or []:
        item = _as_dict(item)
        block: dict[str, Any] = {"type": "piece", "name": item.get("name") or "Piece"}
        if item.get("itemId"):
            block["itemId"] = item["itemId"]
        if item.get("price") is not None:
            block["price"] = item["price"]
        if item.get("size"):
            block["size"] = item["size"]
        if item.get("stock") is not None:
            block["stock"] = item["stock"]
        if item.get("imageUrl"):
            block["imageUrl"] = item["imageUrl"]
        blocks.append(block)

    for look in visual.get("looks") or []:
        look = _as_dict(look)
        block = {"type": "look"}
        if look.get("imageUrl"):
            block["imageUrl"] = look["imageUrl"]
        if look.get("name"):
            block["name"] = look["name"]
        if look.get("text"):
            block["text"] = look["text"]
        blocks.append(block)

    return blocks


# ----------------------------------------------------------------------- Lina (commerce)


def build_lina_blocks(commerce_output: Any) -> list[dict[str, Any]]:
    """Map the Commerce Agent's ``commerce`` output into content blocks.

    A ``status == "stub"`` output (the pre-Slice 3 placeholder) has no real content and
    yields no blocks. A ``summary`` becomes a ``text`` block, a ``payment`` becomes a
    ``payment`` block, and a ``courier`` becomes a ``courier`` block.

    ``sign_off`` is deliberately never emitted here: a SignOff is a first-class
    human-in-the-loop message (``kind == SignOff``) created by the commerce approval flow,
    not a generic persona message.
    """
    commerce = _as_dict(commerce_output)
    if commerce.get("status") == "stub":
        return []

    blocks: list[dict[str, Any]] = []

    summary = commerce.get("summary")
    if summary:
        blocks.append({"type": "text", "text": str(summary)})

    payment = _as_dict(commerce.get("payment"))
    if payment:
        block: dict[str, Any] = {"type": "payment"}
        if payment.get("amount") is not None:
            block["amount"] = payment["amount"]
        if payment.get("status"):
            block["status"] = payment["status"]
        if payment.get("url"):
            block["url"] = payment["url"]
        blocks.append(block)

    courier = _as_dict(commerce.get("courier"))
    if courier:
        block = {"type": "courier"}
        if courier.get("status"):
            block["status"] = courier["status"]
        if courier.get("carrier"):
            block["carrier"] = courier["carrier"]
        blocks.append(block)

    return blocks


# ------------------------------------------------------------------- Aveline (entry point)


def build_aveline_blocks(
    output: Any,
    *,
    specialist_spoke: bool = False,
) -> list[dict[str, Any]]:
    """Map the concierge outcome into Aveline's content blocks.

    Aveline is the entry point, and she speaks only when nobody else does:

    - A **clarification** she asked is rendered verbatim and takes precedence over everything.
    - Her own **reply** is rendered when no specialist produced content. The reply comes from the
      supervisor, which writes one for a conversational message that has nothing to route, and
      which grounds a platform answer in retrieved handbook excerpts (ADR-025).
    - Otherwise she is **silent**. A routing summary ("Treated this as a product search.") is not
      content: it describes what the orchestrator did instead of answering the person, and it read
      as a bug when it was the only thing a customer ever saw.

    Args:
        output: The concierge ``AgentResponse.output``.
        specialist_spoke: True when at least one specialist emitted content this run. A
            specialist's answer and Aveline's fallback reply must not both land in one thread.
    """
    out = _as_dict(output)

    clarification = out.get("clarification")
    if clarification:
        blocks = build_clarification_blocks(clarification)
        if blocks:
            return blocks

    if specialist_spoke:
        return []

    reply = out.get("reply")
    if isinstance(reply, str) and reply.strip():
        blocks: list[dict[str, Any]] = [{"type": "text", "text": reply.strip()}]
        # The citation is a block of its own, not a line appended to the prose: a frontend can make
        # a block into links, and cannot reliably find links inside model-written text (ADR-025).
        sources = _handbook_sources(out.get("handbook_sources"))
        if sources:
            blocks.append({"type": "sources", "items": sources})
        return blocks

    return []


def _handbook_sources(sources: Any) -> list[dict[str, str]]:
    """A handbook-grounded answer's citations, as structured items (ADR-025).

    Built from the chunks that were actually retrieved, never from the model's text: a model asked
    to cite will sometimes cite something it did not use, and a fabricated source is worse than no
    source. One entry per page, because five chunks from one page are one source.
    """
    if not isinstance(sources, list):
        return []

    items: list[dict[str, str]] = []
    seen: set[str] = set()

    for source in sources:
        if not isinstance(source, dict):
            continue

        title = str(source.get("title") or "").strip()
        if not title or title in seen:
            continue
        seen.add(title)

        item: dict[str, str] = {"title": title}
        url = str(source.get("url") or "").strip()
        if url:
            item["url"] = url
        heading = str(source.get("heading") or "").strip()
        if heading:
            item["heading"] = heading
        items.append(item)

    return items


def build_clarification_blocks(clarification: Any) -> list[dict[str, Any]]:
    """Render a clarification as content blocks.

    Three shapes (ADR-023):

    - ``ambiguous`` -> a ``choice`` block listing candidate customers to tap.
    - ``not_found`` -> a ``text`` block asking the staff for a phone number. Reached only when the
      lookup came from an explicit staff ``@mention``; an inbound sender's own number is already
      known, so that path never asks.
    - ``asked`` -> a ``text`` block carrying the supervisor's own question, used when it saw the
      transcript and decided it could not proceed.

    Returns an empty list when the clarification carries no renderable content.
    """
    out = _as_dict(clarification)
    kind = out.get("kind")

    if kind == "asked":
        question = out.get("question")
        if isinstance(question, str) and question.strip():
            return [{"type": "text", "text": question.strip()}]
        return []

    if kind == "ambiguous":
        options: list[dict[str, Any]] = []
        for candidate in out.get("candidates") or []:
            option: dict[str, Any] = {
                "customerId": str((candidate or {}).get("customer_id", "")),
                "fullName": (candidate or {}).get("full_name"),
                "status": (candidate or {}).get("status") or "new",
            }
            last_visit = (candidate or {}).get("last_visit_at")
            if last_visit is not None:
                option["lastVisitAt"] = str(last_visit)
            options.append(option)

        if not options:
            return []

        return [
            {
                "type": "choice",
                "prompt": "I found a few customers that could match. Which one did you mean?",
                "options": options,
            }
        ]

    if kind == "not_found":
        return [
            {
                "type": "text",
                "text": "I couldn't find a customer with that name. Could you share their phone number so I can look them up?",
            }
        ]

    return []
