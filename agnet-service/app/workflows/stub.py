"""Stub LangGraph workflow used to exercise the agent service infrastructure.

The real agent graphs (customer_memory, visual_insight, commerce) land in later
slices. Until then this minimal graph lets the query and streaming endpoints be
wired and tested end-to-end without an LLM.
"""

import logging
from typing import TypedDict

from langgraph.graph import END, START, StateGraph

logger = logging.getLogger("aveline.agent.stub")


class StubState(TypedDict):
    """State for the stub graph."""

    query: str
    result: str


def _echo_node(state: StubState) -> dict:
    """Echo the query back as the result."""
    return {"result": f"echo: {state['query']}"}


def build_stub_graph():
    """Build and compile the stub graph."""
    graph = StateGraph(StubState)
    graph.add_node("echo", _echo_node)
    graph.add_edge(START, "echo")
    graph.add_edge("echo", END)
    return graph.compile()


def run_stub(query: str) -> dict:
    """Run the stub graph synchronously and return the final state."""
    compiled = build_stub_graph()
    return compiled.invoke({"query": query})
