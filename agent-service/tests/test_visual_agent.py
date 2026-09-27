"""Unit tests for Visual Insight Agent (Elle — Slice 2).

The rule-based non-graph entry points (``run`` and its ``_handle_*`` siblings) were deleted
in U3.1 rather than ported (strategy §1); their absence is asserted in
``tests/agents/test_visual_insight_graph.py``. What remains here is the live
``parse_visual_intent`` node that the graph wires.
"""

from unittest.mock import AsyncMock

import pytest

from app.agents.visual_insight.nodes import VisualInsightAgent, _describe_what_was_seen


class TestVisualAgent:
    @pytest.mark.asyncio
    async def test_parse_visual_intent_detects_diverse_shades_and_themes(self):
        agent = VisualInsightAgent(tools=AsyncMock(), llm=None)

        # 1. Pastel Lavender with Dress / Gown category
        res1 = await agent.parse_visual_intent({"message": "Find me a lavender dress for a wedding", "org_id": "org-1"})
        assert res1["search_criteria"]["color"] == "Lavender"
        assert res1["search_criteria"]["occasion"] == "Wedding"
        assert res1["search_criteria"]["category"] == "Gowns"

        # 2. Earthy Terracotta with Saree category
        res2 = await agent.parse_visual_intent({"message": "Looking for a burnt terracotta saree", "org_id": "org-1"})
        assert res2["search_criteria"]["color"] == "Burnt Terracotta"
        assert res2["search_criteria"]["category"] == "Sarees"

        # 3. Rich Berries Burgundy with Gowns category
        res3 = await agent.parse_visual_intent({"message": "Show me royal burgundy gowns for a gala", "org_id": "org-1"})
        assert res3["search_criteria"]["color"] == "Royal Burgundy"
        assert res3["search_criteria"]["occasion"] == "Gala"
        assert res3["search_criteria"]["category"] == "Gowns"

        # 4. Pastel Theme Palette Mapping with Lehengas category
        res4 = await agent.parse_visual_intent({"message": "I need pastel lehengas for a reception", "org_id": "org-1"})
        assert res4["search_criteria"]["color_theme"] == "Pastels"
        assert res4["search_criteria"]["occasion"] == "Reception"
        assert res4["search_criteria"]["category"] == "Lehengas"

        # 5. Blue Saree query (user scenario: does not match other garment types)
        res5 = await agent.parse_visual_intent({"message": "do u have blue saree in stock", "org_id": "org-1"})
        assert res5["search_criteria"]["color"] == "Blue"
        assert res5["search_criteria"]["category"] == "Sarees"

        # 6. Red Saree query
        res6 = await agent.parse_visual_intent({"message": "do we have any red saree in stock", "org_id": "org-1"})
        assert res6["search_criteria"]["color"] == "Red"
        assert res6["search_criteria"]["category"] == "Sarees"
        assert "query" not in res6["search_criteria"]


class TestDescribeWhatWasSeen:
    """The analysed image becomes the words the customer reads back.

    The acceptance test for the inbound-image lane is that a reply refers to what is actually in
    the picture, and the analysis was being computed on every run without any reply-composing node
    reading it. These cover that rendering, including the cases that must stay silent so a
    text-only reply is unchanged.
    """

    def test_it_reads_the_analysed_attributes_not_the_message(self):
        seen = _describe_what_was_seen(
            {
                "message": "what do you think?",
                "image_attributes": {
                    "category": "Saree",
                    "primary_color": "Deep Crimson",
                    "fabric": "Silk",
                },
            }
        )

        assert seen == "deep crimson silk saree"

    def test_it_tolerates_a_partial_analysis(self):
        assert _describe_what_was_seen({"image_attributes": {"primary_color": "Emerald"}}) == "emerald"
        assert _describe_what_was_seen({"image_attributes": {"category": "Gown"}}) == "gown"

    def test_the_parse_fallback_colour_is_not_an_observation(self):
        """"Neutral" is what the parser substitutes when it found no colour.

        Describing a garment as "a neutral silk saree" reads as something the model saw while
        carrying none of it, so the fallback is dropped rather than spoken.
        """
        assert _describe_what_was_seen(
            {"image_attributes": {"category": "Saree", "primary_color": "Neutral", "fabric": "Silk"}}
        ) == "silk saree"

    def test_no_analysis_means_nothing_to_say(self):
        """No media, a denied call or a failed one all leave the reply on the text path."""
        assert _describe_what_was_seen({}) is None
        assert _describe_what_was_seen({"image_attributes": None}) is None
        assert _describe_what_was_seen({"image_attributes": {}}) is None
        assert _describe_what_was_seen({"image_attributes": {"primary_color": "Neutral"}}) is None
        # A malformed value must not reach the reply as the literal "none".
        assert _describe_what_was_seen({"image_attributes": "not-a-dict"}) is None

    @pytest.mark.asyncio
    async def test_search_inventory_filters_out_mismatched_category(self):
        """When searching for a blue saree, a blue evening gown must not be returned."""
        mock_registry = AsyncMock()
        # Mock registry returns all blue items (e.g. backend broad search fallback)
        mock_registry.search_inventory.return_value = {
            "items": [
                {
                    "itemId": "item-gown-1",
                    "itemName": "Powder Blue Duchess Satin Luminous Evening Gown",
                    "category": "Gowns",
                    "color": "Powder Blue",
                    "stock": 4,
                    "price": 1250.0,
                },
                {
                    "itemId": "item-saree-1",
                    "itemName": "Royal Blue Kanjivaram Silk Saree",
                    "category": "Sarees",
                    "color": "Royal Blue",
                    "stock": 2,
                    "price": 35000.0,
                },
            ]
        }

        agent = VisualInsightAgent(registry=mock_registry, llm=None)
        state = {
            "message": "do u have blue saree in stock",
            "org_id": "org-1",
            "search_criteria": {
                "organizationId": "org-1",
                "query": "do u have blue saree in stock",
                "color": "Blue",
                "category": "Sarees",
            },
        }

        res = await agent.search_inventory(state)
        matched = res["matched_items"]
        assert len(matched) == 1
        assert matched[0]["itemId"] == "item-saree-1"
        assert matched[0]["name"] == "Royal Blue Kanjivaram Silk Saree"

    @pytest.mark.asyncio
    async def test_search_inventory_filters_out_mismatched_colors(self):
        """When searching for a red saree, peacock teal and terracotta sarees must be pruned."""
        mock_registry = AsyncMock()
        mock_registry.search_inventory.return_value = {
            "items": [
                {
                    "itemId": "item-red-1",
                    "itemName": "Crimson Red Pure Mulberry Silk Banarasi Silk Brocade Saree",
                    "category": "Sarees",
                    "color": "Crimson Red",
                    "stock": 4,
                },
                {
                    "itemId": "item-red-2",
                    "itemName": "Deep Crimson Pure Mulberry Silk Banarasi Silk Brocade Saree",
                    "category": "Sarees",
                    "color": "Deep Crimson",
                    "stock": 4,
                },
                {
                    "itemId": "item-teal-1",
                    "itemName": "Peacock Teal Pure Mulberry Silk Banarasi Silk Brocade Saree",
                    "category": "Sarees",
                    "color": "Peacock Teal",
                    "stock": 4,
                },
                {
                    "itemId": "item-terra-1",
                    "itemName": "Terracotta Pure Mulberry Silk Banarasi Silk Brocade Saree",
                    "category": "Sarees",
                    "color": "Terracotta",
                    "stock": 4,
                },
            ]
        }

        agent = VisualInsightAgent(registry=mock_registry, llm=None)
        state = {
            "message": "do we have any red saree in stock",
            "org_id": "org-1",
            "search_criteria": {
                "organizationId": "org-1",
                "color": "Red",
                "category": "Sarees",
            },
        }

        res = await agent.search_inventory(state)
        matched = res["matched_items"]
        assert len(matched) == 2
        matched_ids = [m["itemId"] for m in matched]
        assert "item-red-1" in matched_ids
        assert "item-red-2" in matched_ids
        assert "item-teal-1" not in matched_ids
        assert "item-terra-1" not in matched_ids

    def test_parse_image_attributes_dict_with_multi_item_clothing(self):
        """Image attributes parser extracts multi-item decomposed clothing detections."""
        from app.tools.inventory.image_tools import parse_image_attributes_dict

        raw_data = {
            "success": True,
            "category": "top",
            "primary_color": "Navy Blue",
            "items": [
                {
                    "clothing_type": "Tailored Blazer",
                    "category": "outerwear",
                    "primary_color": "Navy Blue",
                    "color_hex": "#1e293b",
                    "secondary_colors": ["gold"],
                    "pattern": "solid",
                    "material": "wool",
                    "style": "formal",
                    "confidence": 0.98,
                },
                {
                    "clothing_type": "Trousers",
                    "category": "bottom",
                    "primary_color": "Charcoal Grey",
                    "color_hex": "#334155",
                    "secondary_colors": [],
                    "pattern": "solid",
                    "material": "linen",
                    "style": "formal",
                    "confidence": 0.95,
                }
            ]
        }

        attrs = parse_image_attributes_dict(raw_data)
        assert attrs.category == "top"
        assert attrs.primary_color == "Navy Blue"
        assert len(attrs.detected_items) == 2
        assert attrs.detected_items[0].clothing_type == "Tailored Blazer"
        assert attrs.detected_items[0].category == "outerwear"
        assert attrs.detected_items[0].primary_color == "Navy Blue"
        assert attrs.detected_items[1].clothing_type == "Trousers"
        assert attrs.detected_items[1].category == "bottom"
        assert attrs.detected_items[1].primary_color == "Charcoal Grey"

