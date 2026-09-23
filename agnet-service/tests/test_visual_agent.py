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

        # 1. Pastel Lavender Dress
        res1 = await agent.parse_visual_intent({"message": "Find me a lavender dress for a wedding", "org_id": "org-1"})
        assert res1["search_criteria"]["color"] == "Lavender"
        assert res1["search_criteria"]["category"] == "Dress"
        assert res1["search_criteria"]["occasion"] == "Wedding"

        # 2. Earthy Terracotta Saree
        res2 = await agent.parse_visual_intent({"message": "Looking for a burnt terracotta saree", "org_id": "org-1"})
        assert res2["search_criteria"]["color"] == "Burnt Terracotta"
        assert res2["search_criteria"]["category"] == "Saree"

        # 3. Rich Berries Burgundy Gowns
        res3 = await agent.parse_visual_intent({"message": "Show me royal burgundy gowns for a gala", "org_id": "org-1"})
        assert res3["search_criteria"]["color"] == "Royal Burgundy"
        assert res3["search_criteria"]["category"] == "Gown"
        assert res3["search_criteria"]["occasion"] == "Gala"

        # 4. Pastel Theme Palette Mapping with Lehengas
        res4 = await agent.parse_visual_intent({"message": "I need pastel lehengas for a reception", "org_id": "org-1"})
        assert res4["search_criteria"]["color_theme"] == "Pastels"
        assert res4["search_criteria"]["category"] == "Lehenga"
        assert res4["search_criteria"]["occasion"] == "Reception"

    @pytest.mark.asyncio
    async def test_parse_visual_intent_extracts_dress_and_sizing(self):
        agent = VisualInsightAgent(tools=AsyncMock(), llm=None)

        # "is a dress is available"
        res = await agent.parse_visual_intent({"message": "is a dress is available", "org_id": "org-1"})
        assert res["search_criteria"]["category"] == "Dress"
        assert res["search_criteria"]["query"] == "is a dress is available"

        # Sizing and category
        res2 = await agent.parse_visual_intent({"message": "Looking for a UK 10 emerald silk gown", "org_id": "org-1"})
        assert res2["search_criteria"]["category"] == "Gown"
        assert res2["search_criteria"]["color"] == "Emerald"
        assert res2["search_criteria"]["size"] == "UK 10"

    @pytest.mark.asyncio
    async def test_search_inventory_fallback_when_filtered_empty(self):
        tools = AsyncMock()
        # First call (with specific category/color filter) returns empty
        # Second call (with relaxed query) returns items
        tools.search_inventory.side_effect = [
            {"items": []},
            {"items": [{"itemId": "item-123", "name": "Peach Gown", "price": 45000, "stock": 2}]},
        ]
        agent = VisualInsightAgent(tools=tools, llm=None)

        state = {
            "org_id": "org-1",
            "search_criteria": {
                "organizationId": "org-1",
                "category": "Dress",
                "color": "Peach",
                "query": "is there a peach dress",
            },
        }
        res = await agent.search_inventory(state)
        assert len(res["matched_items"]) == 1
        assert res["matched_items"][0]["name"] == "Peach Gown"
        assert tools.search_inventory.call_count == 2
