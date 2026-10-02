"""LangGraph sub-graph definition for Visual Insight Agent (Elle — Slice 2).

Compiles the visual intelligence and sourcing graph:
    START -> parse_visual_intent -> analyze_image -> search_inventory -> compose_looks -> (check_sourcing if empty) -> compose_output -> END
"""

from typing import Any

from langgraph.graph import END, START, StateGraph

from app.agents.visual_insight.nodes import VisualInsightAgent, image_analysis_blocked
from app.agents.visual_insight.state import VisualAgentState


def build_visual_graph(registry: Any, llm: Any = None) -> Any:
    """Compile and return the Visual Insight Agent graph bound to ``registry`` and optional ``llm``."""
    agent = VisualInsightAgent(registry, llm=llm)

    graph = StateGraph(VisualAgentState)

    graph.add_node("parse_visual_intent", agent.parse_visual_intent)
    graph.add_node("analyze_image", agent.analyze_image)
    graph.add_node("search_inventory", agent.search_inventory)
    graph.add_node("compose_looks", agent.compose_looks)
    graph.add_node("check_sourcing", agent.check_sourcing)
    graph.add_node("compose_output", agent.compose_output)

    graph.add_edge(START, "parse_visual_intent")
    graph.add_edge("parse_visual_intent", "analyze_image")
    graph.add_edge("analyze_image", "search_inventory")
    graph.add_edge("search_inventory", "compose_looks")

    graph.add_conditional_edges(
        "compose_looks",
        _route_after_looks,
        {"compose": "compose_output", "sourcing": "check_sourcing"},
    )
    graph.add_edge("check_sourcing", "compose_output")
    graph.add_edge("compose_output", END)

    return graph.compile()


def _route_after_looks(state: VisualAgentState) -> str:
    """If in-stock items were matched, proceed to compose output; otherwise check sourcing.

    A failed lookup, search failure, or skipped state never reaches sourcing: "we could not check"
    must not become "we do not have it", because the sourcing path turns an empty result into exactly
    that claim.

    The same holds when the vision analysis the run depended on was denied or failed
    (:func:`app.agents.visual_insight.nodes.image_analysis_blocked`). The agent never saw the
    picture, so "no in-stock pieces matched" is not a fact it has. That case used to raise a
    sourcing request and tell the customer "we do not have that piece in stock right now" from an
    image it could not read (gap A1).
    """
    if state.get("status") == "skipped":
        return "compose"
    if state.get("search_failed") or image_analysis_blocked(state):
        return "compose"
    if state.get("matched_items") and len(state["matched_items"]) > 0:
        return "compose"
    return "sourcing"
