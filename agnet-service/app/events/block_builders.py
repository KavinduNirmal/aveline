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


# ----------------------------------------------------------------------- Aveline (summary)


def build_aveline_blocks(output: Any) -> list[dict[str, Any]]:
    """Map the concierge outcome into Aveline's summary ``Note`` text block.

    The summary is intent-aware and names the customer when the memory agent resolved one,
    but it deliberately does not duplicate Ava's rich blocks (brief/memories/draft live in
    Ava's own message).
    """
    out = _as_dict(output)
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
