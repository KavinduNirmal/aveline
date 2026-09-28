"""LangGraph routing helpers for Visual Insight Agent workflows."""

from typing import Any


def route_after_visual(state: dict[str, Any]) -> str:
    """Route to commerce if visual output requires commerce actions; otherwise formulate.

    Inspects both top-level ``requires_commerce`` flag and nested ``visual_output.requires_commerce``.
    """
    if state.get("requires_commerce"):
        return "commerce"

    visual_output = state.get("visual_output")
    if isinstance(visual_output, dict) and visual_output.get("requires_commerce"):
        return "commerce"

    return "formulate"
