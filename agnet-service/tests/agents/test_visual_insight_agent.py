"""Unit tests for VisualInsightAgent business behavior (TDD)."""

from unittest.mock import AsyncMock
import pytest

from app.agents.visual_insight.nodes import VisualInsightAgent


@pytest.mark.asyncio
async def test_item_search_success():
    tools = AsyncMock()

    tools.search_inventory.return_value = [
        {
            "item_id": "item-1",
            "item_name": "Emerald Saree",
            "price": 45000,
            "stock": 3,
        }
    ]

    agent = VisualInsightAgent(
        tools=tools,
        llm=AsyncMock(),
    )

    state = {
        "org_id": "org-123",
        "customer_id": "cust-123",
        "intent_type": "item_search",
        "parsed_intent": {
            "color": "emerald",
            "budget": 50000,
        },
    }

    result = await agent.run(state)

    assert result["status"] == "success"
    assert len(result["found_items"]) == 1


@pytest.mark.asyncio
async def test_no_inventory_with_reference_image_creates_sourcing_request():
    tools = AsyncMock()

    tools.search_inventory.return_value = []

    tools.create_sourcing_request.return_value = {
        "id": "source-1",
        "status": "pending",
    }

    agent = VisualInsightAgent(
        tools=tools,
        llm=AsyncMock(),
    )

    state = {
        "org_id": "org-123",
        "customer_id": "cust-123",
        "intent_type": "item_search",
        "parsed_intent": {
            "reference_image_url": "https://example.com/dress.jpg",
        },
    }

    result = await agent.run(state)

    assert result["status"] == "success"
    assert result["sourcing_suggestion"]["status"] == "pending"
    assert result["action_required"] == "review_sourcing_request"


@pytest.mark.asyncio
async def test_no_inventory_without_reference_returns_no_results():
    tools = AsyncMock()
    tools.search_inventory.return_value = []

    agent = VisualInsightAgent(
        tools=tools,
        llm=AsyncMock(),
    )

    state = {
        "org_id": "org-123",
        "customer_id": "cust-123",
        "intent_type": "item_search",
        "parsed_intent": {
            "color": "red",
            "category": "blouse",
        },
    }

    result = await agent.run(state)

    assert result["status"] == "no_results"


@pytest.mark.asyncio
async def test_image_analysis_returns_attributes():
    tools = AsyncMock()

    tools.analyze_product_image.return_value = {
        "color": "emerald",
        "fabric": "silk",
        "style": "elegant",
    }

    agent = VisualInsightAgent(
        tools=tools,
        llm=AsyncMock(),
    )

    state = {
        "org_id": "org-123",
        "intent_type": "image_analysis",
        "image_url": "https://example.com/product.jpg",
    }

    result = await agent.run(state)

    assert result["status"] == "success"
    assert result["analyzed_image"]["color"] == "emerald"


@pytest.mark.asyncio
async def test_image_analysis_without_image_returns_error():
    agent = VisualInsightAgent(
        tools=AsyncMock(),
        llm=AsyncMock(),
    )

    result = await agent.run({
        "org_id": "org-123",
        "intent_type": "image_analysis",
    })

    assert result["status"] == "error"


@pytest.mark.asyncio
async def test_new_inventory_item_generates_customer_matches():
    tools = AsyncMock()
    tools.analyze_product_image.return_value = {
        "color": "emerald",
        "fabric": "silk",
        "style": "elegant",
    }
    tools.generate_customer_matches.return_value = [
        {
            "customerId": "cust-1",
            "matchScore": 0.95,
            "stylePreferences": ["evening"],
        }
    ]

    agent = VisualInsightAgent(
        tools=tools,
        llm=AsyncMock(),
    )

    state = {
        "org_id": "org-123",
        "intent_type": "image_analysis",
        "image_url": "https://example.com/product.jpg",
        "item_id": "item-123",
    }

    result = await agent.run(state)

    assert result["status"] == "success"
    assert result["analyzed_image"]["color"] == "emerald"
    assert len(result["customer_matches"]) == 1
    assert result["customer_matches"][0]["customerId"] == "cust-1"
    tools.generate_customer_matches.assert_called_once_with("item-123", "org-123")


@pytest.mark.asyncio
async def test_image_analysis_without_item_id_does_not_call_customer_matches():
    tools = AsyncMock()
    tools.analyze_product_image.return_value = {
        "color": "emerald",
        "fabric": "silk",
        "style": "elegant",
    }

    agent = VisualInsightAgent(
        tools=tools,
        llm=AsyncMock(),
    )

    state = {
        "org_id": "org-123",
        "intent_type": "image_analysis",
        "image_url": "https://example.com/product.jpg",
    }

    result = await agent.run(state)

    assert result["status"] == "success"
    assert "customer_matches" not in result or result.get("customer_matches") is None
    tools.generate_customer_matches.assert_not_called()


@pytest.mark.asyncio
async def test_outfit_composition_uses_requested_occasion():
    tools = AsyncMock()

    tools.compose_outfit.return_value = {
        "name": "Elegant Emerald Look",
        "occasion": "wedding",
        "total_price": 65000,
    }

    agent = VisualInsightAgent(
        tools=tools,
        llm=AsyncMock(),
    )

    result = await agent.run({
        "org_id": "org-123",
        "customer_id": "cust-123",
        "intent_type": "outfit_composition",
        "parsed_intent": {
            "occasion": "wedding",
        },
    })

    tools.compose_outfit.assert_awaited_once_with(
        "org-123",
        "cust-123",
        "wedding",
    )

    assert result["outfit_proposal"]["total_price"] == 65000


@pytest.mark.asyncio
async def test_outfit_does_not_include_sold_out_items():
    tools = AsyncMock()
    tools.compose_outfit.return_value = {
        "name": "Summer Gala Look",
        "occasion": "gala",
        "total_price": 45000,
        "items": [
            {"item_id": "item-in-stock", "item_name": "Silk Gown", "stock": 2, "price": 30000},
            {"item_id": "item-sold-out", "item_name": "Velvet Stole", "stock": 0, "price": 15000},
        ],
    }

    agent = VisualInsightAgent(tools=tools, llm=AsyncMock())
    result = await agent.run({
        "org_id": "org-123",
        "customer_id": "cust-123",
        "intent_type": "outfit_composition",
        "parsed_intent": {"occasion": "gala"},
    })

    assert result["status"] == "success"
    outfit_items = result["outfit_proposal"]["items"]
    assert len(outfit_items) == 1
    assert outfit_items[0]["item_id"] == "item-in-stock"
    assert all(i.get("stock", 0) > 0 for i in outfit_items)


@pytest.mark.asyncio
async def test_recommended_item_contains_price():
    tools = AsyncMock()
    tools.search_inventory.return_value = [
        {"item_id": "item-priced", "item_name": "Emerald Saree", "price": 45000, "stock": 3},
        {"item_id": "item-unpriced", "item_name": "Draft Sample", "price": None, "stock": 2},
        {"item_id": "item-invalid-price", "item_name": "Zero Sample", "price": -10, "stock": 1},
    ]

    agent = VisualInsightAgent(tools=tools, llm=AsyncMock())
    result = await agent.run({
        "org_id": "org-123",
        "intent_type": "item_search",
        "parsed_intent": {"color": "emerald"},
    })

    assert result["status"] == "success"
    assert len(result["found_items"]) == 1
    assert result["found_items"][0]["item_id"] == "item-priced"
    assert result["found_items"][0]["price"] == 45000


@pytest.mark.asyncio
async def test_customer_matching_considers_preferences():
    tools = AsyncMock()
    tools.analyze_product_image.return_value = {
        "color": "emerald",
        "fabric": "silk",
        "style": "elegant",
    }
    tools.generate_customer_matches.return_value = [
        {"customerId": "cust-1", "matchScore": 0.98, "preferred_colors": ["emerald"]},
        {"customerId": "cust-2", "matchScore": 0.40, "preferred_colors": ["navy"]},
    ]

    agent = VisualInsightAgent(tools=tools, llm=AsyncMock())
    result = await agent.run({
        "org_id": "org-123",
        "intent_type": "image_analysis",
        "image_url": "https://example.com/emerald.jpg",
        "item_id": "item-123",
        "preferences": {"preferred_colors": ["emerald"]},
    })

    assert result["status"] == "success"
    assert len(result["customer_matches"]) > 0
    tools.generate_customer_matches.assert_called_once()


@pytest.mark.asyncio
async def test_outfit_total_does_not_exceed_budget():
    tools = AsyncMock()
    tools.compose_outfit.return_value = {
        "name": "Luxury Gala Ensemble",
        "occasion": "gala",
        "total_price": 75000,
        "items": [
            {"item_id": "item-1", "price": 40000, "stock": 2},
            {"item_id": "item-2", "price": 35000, "stock": 1},
        ],
    }

    agent = VisualInsightAgent(tools=tools, llm=AsyncMock())
    result = await agent.run({
        "org_id": "org-123",
        "customer_id": "cust-123",
        "intent_type": "outfit_composition",
        "parsed_intent": {"occasion": "gala", "budget": 50000},
    })

    assert result["status"] == "success"
    assert result["outfit_proposal"]["total_price"] <= 50000
    assert len(result["outfit_proposal"]["items"]) == 1
    assert result["outfit_proposal"]["items"][0]["item_id"] == "item-1"


@pytest.mark.asyncio
async def test_agent_never_receives_inventory_from_another_org():
    tools = AsyncMock()
    tools.search_inventory.return_value = [
        {"item_id": "item-org1", "org_id": "org-123", "item_name": "Emerald Saree", "price": 45000, "stock": 3},
        {"item_id": "item-org2", "org_id": "org-FOREIGN", "item_name": "Foreign Item", "price": 30000, "stock": 5},
    ]

    agent = VisualInsightAgent(tools=tools, llm=AsyncMock())
    result = await agent.run({
        "org_id": "org-123",
        "intent_type": "item_search",
        "parsed_intent": {"color": "emerald"},
    })

    assert result["status"] == "success"
    assert len(result["found_items"]) == 1
    assert result["found_items"][0]["item_id"] == "item-org1"
    assert all(item.get("org_id", "org-123") == "org-123" for item in result["found_items"])







