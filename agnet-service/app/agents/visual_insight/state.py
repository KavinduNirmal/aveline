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
    image_url: str | None
    intent_type: str | None
    preferences: dict[str, Any] | None

    # Working intermediate state
    image_attributes: dict[str, Any] | None
    search_criteria: dict[str, Any] | None
    matched_items: list[dict[str, Any]]
    composed_looks: list[dict[str, Any]]
    sourcing_request: dict[str, Any] | None
    suggestion: str | None

    # Final Sub-graph Output
    output: dict[str, Any] | None
    status: str | None
    reason: str | None
