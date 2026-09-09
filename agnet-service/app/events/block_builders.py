"""Translate real agent output into Salon content blocks (ADR-016).

The concierge ``AgentResponse.output`` carries structured sub-output per specialist
(e.g. the Customer Memory Agent's ``memory`` dict). These builders map that structure into
the typed content blocks documented in ``docs/architecture/inbox.md`` §5 so the Salon
renders real content rather than placeholder text.

Only content-producing blocks are emitted; a persona that ran but produced nothing stays
silent (its message is simply omitted by the publisher).
"""

from typing import Any

#: Concierge intent type -> short, human-readable phrase for Aveline's summary.
_INTENT_LABELS: dict[str, str] = {
    "item_search": "a product search",
    "pricing_query": "a pricing question",
    "customer_preference": "a preference lookup",
    "event_query": "an event request",
    "general_inquiry": "a general inquiry",
}


def _intent_label(intent: Any) -> str:
    if isinstance(intent, str):
        return _INTENT_LABELS.get(intent, intent.replace("_", " "))
    return "a general inquiry"


def _as_dict(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


# --------------------------------------------------------------------------- Ava (memory)


def build_ava_blocks(memory_output: Any) -> list[dict[str, Any]]:
    """Map the Customer Memory Agent's ``memory`` output into content blocks.

    Returns an empty list when no real content was produced (e.g. no customer context was
    available, so the agent skipped personalization).

    Blocks (in order):
      1. ``text`` - the interaction brief for the customer (if present).
      2. ``at_a_glance`` - extracted memories as a Category/Content table (if any).
      3. ``suggestion`` - the drafted, customer-facing response (if present).
    """
    memory = _as_dict(memory_output)
    if memory.get("status") == "skipped":
        return []

    blocks: list[dict[str, Any]] = []

    brief = memory.get("interaction_brief")
    if brief:
        blocks.append({"type": "text", "text": str(brief)})

    rows: list[list[str]] = []
    for item in memory.get("extracted_memories") or []:
        content = (item or {}).get("content")
        if content:
            rows.append([str((item or {}).get("category") or "memory"), str(content)])
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


# ----------------------------------------------------------------------- Aveline (summary)


def build_aveline_blocks(output: Any) -> list[dict[str, Any]]:
    """Map the concierge outcome into Aveline's summary ``Note`` text block.

    The summary is intent-aware and names the customer when the memory agent resolved one,
    but it deliberately does not duplicate Ava's rich blocks (brief/memories/draft live in
    Ava's own message). When the orchestrator could not resolve a customer it carries a
    ``clarification`` (ambiguous candidates or not-found) which is rendered instead.
    """
    out = _as_dict(output)

    clarification = out.get("clarification")
    if clarification:
        blocks = build_clarification_blocks(clarification)
        if blocks:
            return blocks

    label = _intent_label(out.get("intent"))

    customer_name: str | None = None
    memory = _as_dict(out.get("memory"))
    if memory.get("status") != "skipped":
        customer = _as_dict(memory.get("customer"))
        customer_name = customer.get("full_name") or customer.get("customer_id")

    if customer_name:
        text = f"Treated this as {label}. I have pulled up what we know on {customer_name} - the details are below."
    else:
        text = f"Treated this as {label}."

    return [{"type": "text", "text": text}]


def build_clarification_blocks(clarification: Any) -> list[dict[str, Any]]:
    """Render a customer-resolution clarification (Issue #161) as content blocks.

    - ``ambiguous`` -> a ``choice`` block listing candidate customers to tap.
    - ``not_found`` -> a ``text`` block asking the staff for a phone number.

    Returns an empty list when the clarification carries no renderable content.
    """
    out = _as_dict(clarification)
    kind = out.get("kind")

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
