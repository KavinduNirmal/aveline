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
    # Conversation context (ADR-023), forwarded verbatim by the orchestrator: the bounded
    # transcript window (oldest first), what fell outside it, and the working set that survives
    # trimming. LangGraph drops state keys a schema does not declare, so the declaration is what
    # makes the orchestrator's copy reach this agent at all.
    history: list[dict[str, Any]]
    thread_summary: str | None
    pinned_slots: dict[str, str]
    channel: str
    direction: str
    # Intent hint from the upstream gate (optional)
    intent_type: str | None
    # True when this request is a STAFF query (no inbound customer message). Staff queries are
    # answered directly; only genuine inbound customer messages get a customer-facing draft.
    staff_query: bool | None
    # Working results
    consent_status: str | None
    profile: dict[str, Any] | None
    #: Set when an explicit staff instruction changed the customer's details. Its presence routes
    #: the run straight to a confirmation instead of a brief and draft.
    applied_customer_update: dict[str, Any] | None
    #: Set when such an instruction could not be applied (duplicate phone, customer not found), so
    #: the run explains the failure rather than silently doing nothing.
    customer_update_error: str | None
    semantic_context: list[dict[str, Any]]
    parsed_intent: dict[str, Any] | None
    extracted_memories: list[dict[str, Any]]
    detected_events: list[dict[str, Any]]
    # Transient parse signals consumed by the persist node (declared so LangGraph can route it).
    _preference_signals: list[dict[str, Any]]
    #: "complaint" | "sentiment" | None - how the customer reported things in this message (A7).
    _experience_signal: str | None
    #: The small facts the model read out of this message - the "nitbits" no deterministic shape
    #: covers (an observation, a resolved pronoun, a constraint). Declared because LangGraph drops
    #: undeclared keys silently: without this the extraction would run, be paid for, and reach
    #: neither `persist` nor the at-a-glance table.
    _extracted_facts: list[dict[str, Any]]
    #: Tokens the extraction call spent. Private because it is not an output: `compose_output` folds
    #: it into the run's single `usage` figure (ADR-010), so the reporter sees one run, not two
    #: calls it would have to know about.
    _extraction_usage: dict[str, Any] | None
    # Token usage captured when an LLM generated the draft (input/output tokens), else None.
    usage: dict[str, Any] | None
    # Output
    output: dict[str, Any] | None
    status: str | None
    reason: str | None
