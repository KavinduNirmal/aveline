"""Unit tests for LangGraph visual insight conditional routing behavior."""

import pytest

from app.agents.visual_insight.routing import route_after_visual


def test_visual_output_requiring_commerce_routes_to_commerce():
    state = {
        "requires_commerce": True
    }

    route = route_after_visual(state)

    assert route == "commerce"


def test_visual_output_without_commerce_routes_to_formulate():
    state = {
        "requires_commerce": False
    }

    route = route_after_visual(state)

    assert route == "formulate"


def test_visual_output_missing_commerce_flag_defaults_to_formulate():
    state = {}

    route = route_after_visual(state)

    assert route == "formulate"


def test_nested_visual_output_requiring_commerce():
    state = {
        "visual_output": {
            "requires_commerce": True,
            "items": [{"item_id": "item-1"}]
        }
    }

    route = route_after_visual(state)

    assert route == "commerce"
