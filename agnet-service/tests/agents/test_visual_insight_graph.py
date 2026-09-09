from unittest.mock import AsyncMock, MagicMock
import pytest

from app.agents.visual_insight.graph import build_visual_graph


@pytest.mark.asyncio
async def test_visual_insight_graph_in_stock_look():
    registry = MagicMock()
    registry.search_inventory = AsyncMock(
        return_value={
            "items": [
                {
                    "itemId": "item-101",
                    "name": "Peach Raw-Silk Drape Gown",
                    "price": 1250.0,
                    "stock": 2,
                    "imageUrl": "https://images.aveline.luxury/gown.jpg",
                },
                {
                    "itemId": "item-102",
                    "name": "Pearl Clasp Stole",
                    "price": 300.0,
                    "stock": 5,
                    "imageUrl": "https://images.aveline.luxury/stole.jpg",
                },
            ]
        }
    )

    graph = build_visual_graph(registry)

    state = {
        "org_id": "org-boutique-1",
        "customer_id": "cust-sarah",
        "message": "I need an outfit for my sister's wedding in Galle",
        "image_url": None,
    }

    result = await graph.ainvoke(state)
    output = result["output"]

    assert output["status"] == "success"
    assert output["agent"] == "visual"
    assert output["ran"] is True
    assert len(output["items"]) == 2
    assert output["items"][0]["name"] == "Peach Raw-Silk Drape Gown"
    assert len(output["looks"]) == 1
    assert "Wedding" in output["looks"][0]["name"]
    assert "Elle curated 2 piece(s)" in output["suggestion"]
    assert output["sourcing_request"] is None


@pytest.mark.asyncio
async def test_visual_insight_graph_with_image_analysis():
    registry = MagicMock()
    registry.analyze_product_image = AsyncMock(
        return_value={
            "category": "Cocktail Dress",
            "primary_color": "Emerald",
            "silhouette": "Slip",
            "fabric": "Silk Satin",
            "occasion": "Cocktail",
            "aesthetic_tags": ["Evening", "Minimalist"],
        }
    )
    registry.search_inventory = AsyncMock(
        return_value={
            "items": [
                {
                    "itemId": "item-202",
                    "name": "Emerald Satin Slip Dress",
                    "price": 750.0,
                    "stock": 1,
                    "imageUrl": "https://images.aveline.luxury/emerald.jpg",
                }
            ]
        }
    )

    graph = build_visual_graph(registry)

    state = {
        "org_id": "org-boutique-1",
        "customer_id": "cust-01",
        "message": "Do you have anything matching this Pinterest photo?",
        "image_url": "https://pinterest.com/pin/emerald-slip.jpg",
    }

    result = await graph.ainvoke(state)
    output = result["output"]

    assert output["status"] == "success"
    assert output["image_attributes"]["category"] == "Cocktail Dress"
    assert output["image_attributes"]["primary_color"] == "Emerald"
    assert len(output["items"]) == 1
    assert output["items"][0]["itemId"] == "item-202"


@pytest.mark.asyncio
async def test_visual_insight_graph_out_of_stock_sourcing():
    registry = MagicMock()
    registry.search_inventory = AsyncMock(return_value={"items": []})
    registry.create_sourcing_request = AsyncMock(
        return_value={"requestId": "src_vintage_99"}
    )

    graph = build_visual_graph(registry)

    state = {
        "org_id": "org-boutique-1",
        "customer_id": "cust-01",
        "message": "Seeking an archival 1994 tailored velvet trench coat in burgundy",
        "image_url": None,
    }

    result = await graph.ainvoke(state)
    output = result["output"]

    assert output["status"] == "pending"
    assert len(output["items"]) == 0
    assert len(output["looks"]) == 0
    assert output["sourcing_request"] is not None
    assert "partner ateliers" in output["suggestion"]
