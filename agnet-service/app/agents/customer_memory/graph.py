"""LangGraph sub-graph for the Customer Memory Agent (Slice 1).

Compiles the memory agent into a checkpointable ``StateGraph``:

    resolve_customer -> check_consent -> parse -> retrieve -> persist -> compose_output

Conditional edges short-circuit to END when the customer cannot be resolved or has revoked
consent (both emit a ``skipped`` status). ``retrieve``/``persist`` require a resolved,
consenting customer; the message-parse nodes always run.

The graph is dependency-injected over a ``ToolRegistry`` so it is fully testable with a stub.
"""

from collections.abc import Callable
from typing import Any

from langchain_core.language_models.chat_models import BaseChatModel
from langgraph.graph import END, START, StateGraph

from app.agents.customer_memory.nodes import CustomerMemoryAgent
from app.agents.customer_memory.state import MemoryAgentState

END_NODE = "end"


def build_memory_graph(
    registry: Any,
    llm: BaseChatModel | None = None,
    org_context: dict[str, Any] | None = None,
) -> Callable[[MemoryAgentState], dict[str, Any]]:
    """Compile and return the Customer Memory Agent graph bound to ``registry``.

    Args:
        registry: A ``ToolRegistry`` (or a test double with the same async methods) used for all
            backend calls.
        llm: An optional chat model used to generate the draft reply. When omitted (or when the
            provider call fails) the agent falls back to its deterministic template.
        org_context: Optional organization context (plan tier, brand voice, rules) injected into
            the memory agent's system prompt.

    Returns:
        A compiled LangGraph ``StateGraph``.
    """
    agent = CustomerMemoryAgent(registry, llm=llm, org_context=org_context)

    graph = StateGraph(MemoryAgentState)

    graph.add_node("resolve_customer", agent.resolve_customer)
    graph.add_node("check_consent", agent.check_consent)
    graph.add_node("parse", agent.parse)
    graph.add_node("retrieve", agent.retrieve)
    graph.add_node("persist", agent.persist)
    graph.add_node("compose_output", agent.compose_output)

    graph.add_edge(START, "resolve_customer")

    # resolve_customer -> end when skipped (no customer context), else check_consent.
    graph.add_conditional_edges(
        "resolve_customer",
        _route_skipped,
        {"continue": "check_consent", "skip": END},
    )
    graph.add_conditional_edges(
        "check_consent",
        _route_skipped,
        {"continue": "parse", "skip": END},
    )

    # parse always runs, then retrieve/persist for a resolved, consenting customer.
    graph.add_conditional_edges(
        "parse",
        _route_can_personalize,
        {"personalize": "retrieve", "compose": "compose_output"},
    )
    graph.add_edge("retrieve", "persist")
    graph.add_edge("persist", "compose_output")
    graph.add_edge("compose_output", END)

    return graph.compile()


def _route_skipped(state: MemoryAgentState) -> str:
    return "skip" if state.get("status") in ("skipped", "out_of_scope") else "continue"


def _route_can_personalize(state: MemoryAgentState) -> str:
    # Personalization (semantic retrieval + memory save) needs a customer. When absent we still
    # compose a message-level output.
    if state.get("customer_id") and state.get("consent_status") != "revoked":
        return "personalize"
    return "compose"
