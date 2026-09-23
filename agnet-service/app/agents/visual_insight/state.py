"""Typed state flowing through the Visual Insight Agent (Elle — Slice 2) sub-graph.

Driven by inbound visual inquiries, occasion requests, moodboards, or product searches.
Produces a ``VisualAgentOutput``-shaped dictionary stored under ``output`` that feeds
``build_elle_blocks`` to render piece, look, and suggestion blocks in The Salon.
"""

from typing import Any, TypedDict


class VisualAgentState(TypedDict, total=False):
    # Inputs
    org_id: str
    customer_id: str | None
    customer_name: str | None
    message: str
    # The reference arm: the API's ``attachments[].reference`` (kind ``attachment`` or
    # ``inventoryImage``, strategy §5.1 S5). Preferred over ``image_url``.
    image_ref_kind: str | None
    image_ref_id: str | None
    # The compatibility bridge: an absolute, single-use, opaque tokenised URL. Never persisted
    # or logged, and used only when no reference is present (strategy §3.5, C14).
    image_url: str | None
    intent_type: str | None
    preferences: dict[str, Any] | None
    channel: str | None
    direction: str | None
    staff_query: bool

    # Working intermediate state
    image_attributes: dict[str, Any] | None
    search_criteria: dict[str, Any] | None
    matched_items: list[dict[str, Any]]
    composed_looks: list[dict[str, Any]]
    sourcing_request: dict[str, Any] | None
    suggestion: str | None
    summary: str | None
    text: str | None

    # Token & usage metrics (ADR-010)
    prompt_tokens: int | None
    completion_tokens: int | None
    total_tokens: int | None

    # Final Sub-graph Output
    output: dict[str, Any] | None
    status: str | None
    reason: str | None

