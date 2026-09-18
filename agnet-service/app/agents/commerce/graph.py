"""LangGraph sub-graph for the Commerce Agent (Slice 3 - Lina).

Compiles the commerce agent into a checkpointable ``StateGraph``:

    START -> evaluate_deal -> [requires_approval?]
                               ├── Yes ──► pause_for_approval ──► END (HITL Interrupt)
                               ├── Rejected ──► handle_rejection ──► END
                               └── No/Approved ──► prepare_settlement ──► END

The graph is dependency-injected over a ``ToolRegistry`` for testability and portability.
"""

from collections.abc import Callable
from typing import Any

from langchain_core.language_models.chat_models import BaseChatModel
from langgraph.graph import END, START, StateGraph

from app.agents.commerce.nodes import CommerceAgent
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
            "end": END,
        },
    )

    graph.add_edge("pause_for_approval", END)
    graph.add_edge("handle_rejection", END)
    graph.add_edge("prepare_settlement", END)

    return graph.compile()


def _route_after_deal_evaluation(state: CommerceAgentState) -> str:
    """Route workflow based on rules evaluation or human approval decision."""
    if state.get("status") == "skipped":
        return "end"

    # Resumed state with explicit owner decision
    decision = state.get("approval_decision")
    if decision == "rejected":
        return "rejected"
    if decision == "approved":
        return "settle"

    # Initial evaluation requiring Human-in-the-Loop approval
    if state.get("requires_approval"):
        return "approval_required"

    return "settle"
