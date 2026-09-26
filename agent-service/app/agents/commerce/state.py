"""Typed state flowing through the Commerce Agent sub-graph (Slice 3 - Lina).

The sub-graph is driven by an order request (line items, pricing, proposed discount,
delivery details, and customer context). It computes margins, checks business rules,
pauses for Human-in-the-Loop (HITL) approval if thresholds are exceeded, and produces
a ``CommerceAgentOutput``-shaped dictionary stored under ``output``.

State is kept JSON-serializable so the graph can be checkpointed and resumed (ADR-002, ADR-016).
"""

from typing import Any, TypedDict


class CommerceAgentState(TypedDict, total=False):
    """State schema for the Commerce Agent sub-graph."""

    # ---------------------------------------------------------------------------
    # Inputs
    # ---------------------------------------------------------------------------
    org_id: str
    order_id: str | None
    customer_id: str | None
    customer_name: str | None
    items: list[dict[str, Any]]
    proposed_discount: float
    delivery_address: str | None
    channel: str
    message: str
    #: Why the line items are here: ``"order"`` when the message asks to buy, ``"quote"`` when it asks
    #: what something would cost (ADR-028). A quote is evaluated by exactly the same rules and must
    #: never pause for approval or settle: a question is not an order, and a run that paused would
    #: create one.
    purpose: str | None
    # Conversation context (ADR-023), forwarded verbatim by the orchestrator for uniformity with
    # the other specialists. Commerce makes no LLM call, so nothing here renders it yet; it is
    # declared so the transport is complete and a future prompt has the window available.
    history: list[dict[str, Any]]
    thread_summary: str | None
    pinned_slots: dict[str, str]
    direction: str | None
    staff_query: bool | None

    # ---------------------------------------------------------------------------
    # Working & Domain Evaluation Results
    # ---------------------------------------------------------------------------
    loyalty_tier: str | None
    subtotal: float
    discount_amount: float
    total: float
    total_cost: float
    margin: float
    is_auto_approved: bool
    requires_approval: bool
    approval_type: str | None
    approval_reason: str | None
    triggered_rules: list[str]
    flags: list[str]
    #: The house rules as the evaluation read them, kept so a quote can apply the same ceiling the
    #: deal evaluation just applied instead of calling for the thresholds twice (ADR-028).
    max_allowed_discount: float
    min_required_margin: float

    # Human-in-the-loop resolution state (when resumed from an approval decision)
    approval_decision: str | None  # "approved", "rejected", "revised"
    approval_comment: str | None
    thread_id: str | None

    # Generated transaction & logistics details
    payment_details: dict[str, Any] | None
    courier_details: dict[str, Any] | None
    summary: str | None

    # Token usage captured if an LLM was invoked (input_tokens, output_tokens)
    usage: dict[str, Any] | None

    # ---------------------------------------------------------------------------
    # Final Output Envelope
    # ---------------------------------------------------------------------------
    output: dict[str, Any] | None
    status: str | None
    reason: str | None
