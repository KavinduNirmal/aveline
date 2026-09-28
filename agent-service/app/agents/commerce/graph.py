"""LangGraph sub-graph for the Commerce Agent (Slice 3 - Lina).

Compiles the commerce agent into a checkpointable ``StateGraph``:

    START -> evaluate_deal -> [requires_approval?]
                               ├── Yes ──► pause_for_approval ──► END (HITL Interrupt)
                               ├── Rejected ──► handle_rejection ──► END
                               ├── No/Approved ──► prepare_settlement ──► END
                               ├── No deal, a discount question ──► explain_discount_ceiling ──► END
                               └── No deal, a quote question ──► present_quote ──► END

The last two arms are the read-only ones (ADR-028): they answer what a discount may be instead of
acting on a deal, so they can never pause, settle or write anything.

The graph is dependency-injected over a ``ToolRegistry`` for testability and portability.
"""

from collections.abc import Callable
from typing import Any

from langchain_core.language_models.chat_models import BaseChatModel
from langgraph.graph import END, START, StateGraph

from app.agents.commerce.nodes import (
    QUOTE_PURPOSE,
    CommerceAgent,
    is_discount_question,
)
from app.agents.commerce.state import CommerceAgentState


def build_commerce_graph(
    registry: Any = None,
    llm: BaseChatModel | None = None,
    org_context: dict[str, Any] | None = None,
) -> Callable[[CommerceAgentState], dict[str, Any]]:
    """Compile and return the Commerce Agent graph bound to ``registry``.

    Args:
        registry: A ``ToolRegistry`` (or test double) used for all internal API calls.
        llm: An optional chat model.
        org_context: Optional organization context.

    Returns:
        A compiled LangGraph ``StateGraph``.
    """
    agent = CommerceAgent(registry=registry, llm=llm, org_context=org_context)

    graph = StateGraph(CommerceAgentState)

    graph.add_node("evaluate_deal", agent.evaluate_deal)
    graph.add_node("explain_discount_ceiling", agent.explain_discount_ceiling)
    graph.add_node("present_quote", agent.present_quote)
    graph.add_node("pause_for_approval", agent.pause_for_approval)
    graph.add_node("handle_rejection", agent.handle_rejection)
    graph.add_node("prepare_settlement", agent.prepare_settlement)

    graph.add_edge(START, "evaluate_deal")

    graph.add_conditional_edges(
        "evaluate_deal",
        _route_after_deal_evaluation,
        {
            "approval_required": "pause_for_approval",
            "settle": "prepare_settlement",
            "rejected": "handle_rejection",
            "ceiling": "explain_discount_ceiling",
            "quote": "present_quote",
            "end": END,
        },
    )

    # Both read-only terminals end the run. Neither writes a payment link, a courier booking or a
    # pause, which is the whole difference between answering and ordering (ADR-028).
    graph.add_edge("explain_discount_ceiling", END)
    graph.add_edge("present_quote", END)
    graph.add_edge("pause_for_approval", END)
    graph.add_edge("handle_rejection", END)
    graph.add_edge("prepare_settlement", END)

    return graph.compile()


def _route_after_deal_evaluation(state: CommerceAgentState) -> str:
    """Route workflow based on rules evaluation or human approval decision.

    A decision is checked **before** the rules, because a decision is the answer to them. Re-evaluating
    after a revision can still report ``requires_approval`` - the revised terms may breach the same
    rules - and routing on that would re-pause a deal the owner has already ruled on.

    ``revised`` settles for the same reason ``approved`` does: the owner changed the terms and
    accepted the result, which is an approval of the revised deal, not a request for another one.

    A **skip** is not silence any more (ADR-028). It means there was no deal to evaluate, and the two
    things a person can still have asked for - what a discount may be, and what a discount on these
    pieces would be - are answerable without one. The quote arm is gated on ``staff_query`` being
    explicitly true for the same reason the tenant-account lane is: it quotes the house's own margin
    policy, and a customer must never be shown it.
    """
    if state.get("status") == "skipped":
        if state.get("purpose") == QUOTE_PURPOSE and state.get("staff_query") is True:
            # A quote whose items could not be resolved is the ceiling case at best; there is
            # nothing to price, so fall through rather than presenting an empty quote.
            return "ceiling" if not state.get("items") else "quote"
        if is_discount_question(state.get("message", "")):
            return "ceiling"
        return "end"

    # Resumed state with explicit owner decision
    decision = state.get("approval_decision")
    if decision == "rejected":
        return "rejected"
    if decision in ("approved", "revised"):
        return "settle"

    # A quote is priced, not committed: it never pauses for a decision and never settles. Checked
    # before the rules because ``requires_approval`` is exactly what a quote reports *about* an order
    # rather than acting on (ADR-028, invariant A7).
    if state.get("purpose") == QUOTE_PURPOSE:
        return "quote" if state.get("staff_query") is True else "end"

    # Initial evaluation requiring Human-in-the-Loop approval
    if state.get("requires_approval"):
        return "approval_required"

    return "settle"
