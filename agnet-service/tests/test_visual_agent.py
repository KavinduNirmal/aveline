"""Unit tests for Visual Insight Agent (Elle — Slice 2).

Tests the agent's core intent branches (item search, image analysis, outfit composition,
sourcing requests, and fallback routing) with mocked tools.
"""

from unittest.mock import AsyncMock
import pytest

from app.agents.visual_insight.nodes import VisualInsightAgent


class TestVisualAgent:
    @pytest.mark.asyncio
    async def test_item_search_success(self):
        tools = AsyncMock()
        tools.search_inventory = AsyncMock(
            return_value=[
                {"item_id": "item-1", "item_name": "Emerald Saree", "price": 45000, "stock": 3}
            ]
        )
        agent = VisualInsightAgent(tools=tools, llm=AsyncMock())

        state = {
            "org_id": "org-123",
            "customer_id": "cust-123",
            "intent_type": "item_search",
            "parsed_intent": {"color": "emerald", "category": "saree", "budget": 50000},
        }

        result = await agent.run(state)

        assert result["status"] == "success"
        assert len(result["found_items"]) == 1
        assert result["found_items"][0]["item_name"] == "Emerald Saree"

    @pytest.mark.asyncio
    async def test_item_search_no_results_suggests_sourcing(self):
        tools = AsyncMock()
        tools.search_inventory = AsyncMock(return_value=[])
        tools.create_sourcing_request = AsyncMock(
            return_value={"id": "sourcing-1", "status": "pending"}
        )
        agent = VisualInsightAgent(tools=tools, llm=AsyncMock())

        state = {
            "org_id": "org-123",
            "customer_id": "cust-123",
            "intent_type": "item_search",
            "parsed_intent": {
                "color": "emerald",
                "reference_image_url": "https://images.luxury/ref.jpg",
            },
        }

        result = await agent.run(state)

        assert result["status"] == "success"
        assert result["sourcing_suggestion"]["status"] == "pending"

    @pytest.mark.asyncio
    async def test_image_analysis_success(self):
        tools = AsyncMock()
        tools.analyze_product_image = AsyncMock(
            return_value={"color": "emerald", "fabric": "silk", "style": "elegant"}
        )
        agent = VisualInsightAgent(tools=tools, llm=AsyncMock())

        state = {
            "org_id": "org-123",
            "customer_id": "cust-123",
            "intent_type": "image_analysis",
            "image_url": "https://images.luxury/photo.jpg",
        }

        result = await agent.run(state)

        assert result["status"] == "success"
        assert result["analyzed_image"]["color"] == "emerald"

    @pytest.mark.asyncio
    async def test_outfit_composition_success(self):
        tools = AsyncMock()
        tools.compose_outfit = AsyncMock(
            return_value={"name": "Elegant Look", "total_price": 65000}
        )
        agent = VisualInsightAgent(tools=tools, llm=AsyncMock())

        state = {
            "org_id": "org-123",
            "customer_id": "cust-123",
            "intent_type": "outfit_composition",
            "parsed_intent": {"occasion": "wedding"},
        }

        result = await agent.run(state)

        assert result["status"] == "success"
        assert result["outfit_proposal"]["total_price"] == 65000

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
