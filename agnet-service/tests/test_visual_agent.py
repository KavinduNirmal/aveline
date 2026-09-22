"""Unit tests for Visual Insight Agent (Elle — Slice 2).

The rule-based non-graph entry points (``run`` and its ``_handle_*`` siblings) were deleted
in U3.1 rather than ported (strategy §1); their absence is asserted in
``tests/agents/test_visual_insight_graph.py``. What remains here is the live
``parse_visual_intent`` node that the graph wires.
"""

from unittest.mock import AsyncMock

import pytest

from app.agents.visual_insight.nodes import VisualInsightAgent


class TestVisualAgent:
    @pytest.mark.asyncio
    async def test_parse_visual_intent_detects_diverse_shades_and_themes(self):
        agent = VisualInsightAgent(tools=AsyncMock(), llm=None)

        # 1. Pastel Lavender
        res1 = await agent.parse_visual_intent({"message": "Find me a lavender dress for a wedding", "org_id": "org-1"})
        assert res1["search_criteria"]["color"] == "Lavender"
        assert res1["search_criteria"]["occasion"] == "Wedding"

        # 2. Earthy Terracotta
        res2 = await agent.parse_visual_intent({"message": "Looking for a burnt terracotta saree", "org_id": "org-1"})
        assert res2["search_criteria"]["color"] == "Burnt Terracotta"

        # 3. Rich Berries Burgundy
        res3 = await agent.parse_visual_intent({"message": "Show me royal burgundy gowns for a gala", "org_id": "org-1"})
        assert res3["search_criteria"]["color"] == "Royal Burgundy"
        assert res3["search_criteria"]["occasion"] == "Gala"

        # 4. Pastel Theme Palette Mapping
        res4 = await agent.parse_visual_intent({"message": "I need pastel lehengas for a reception", "org_id": "org-1"})
        assert res4["search_criteria"]["color_theme"] == "Pastels"
        assert res4["search_criteria"]["occasion"] == "Reception"
