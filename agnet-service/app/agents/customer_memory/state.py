"""Typed state flowing through the Customer Memory Agent sub-graph (Slice 1).

The sub-graph is driven by an inbound customer message plus enough context to resolve the
customer (an organization id and either a customer id or a phone number). It produces a
``MemoryAgentOutput``-shaped dict stored under ``output``.

State is kept JSON-serializable so the graph can be checkpointed (ADR-002).
"""

from typing import Any, TypedDict


class MemoryAgentState(TypedDict, total=False):
    # Inputs
    org_id: str
    customer_id: str | None
    phone_number: str | None
    customer_name: str | None
    message: str
    channel: str
    direction: str
    # Intent hint from the upstream gate (optional)
    intent_type: str | None
    # Working results
    consent_status: str | None
    profile: dict[str, Any] | None
    semantic_context: list[dict[str, Any]]
    parsed_intent: dict[str, Any] | None
    extracted_memories: list[dict[str, Any]]
    detected_events: list[dict[str, Any]]
    # Transient parse signals consumed by the persist node (declared so LangGraph can route it).
    _preference_signals: list[dict[str, Any]]
    # Output
    output: dict[str, Any] | None
    status: str | None
    reason: str | None
