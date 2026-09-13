"""Golden test cases for Visual Insight Agent (Elle — Slice 2)."""

import pytest
from unittest.mock import AsyncMock

from app.agents.visual_insight.nodes import VisualInsightAgent

GOLDEN_CASES = [
    {
        "name": "Find item by color",
        "input": {
            "message": "Find me a green saree",
            "customer_id": "cust-123",
            "org_id": "org-123",
            "intent_type": "item_search",
            "parsed_intent": {"color": "green", "category": "saree"},
        },
        "mock_items": [{"item_id": "item-1", "item_name": "Emerald Silk Saree", "price": 45000, "stock": 2}],
        "expected_status": "success",
        "expected_item_name": "Emerald Silk Saree",
    },
    {
        "name": "Reference image sourcing",
        "input": {
            "message": "Can you get this dress?",
            "customer_id": "cust-123",
            "org_id": "org-123",
            "intent_type": "item_search",
            "parsed_intent": {"reference_image_url": "https://images.luxury/dress.jpg"},
        },
        "mock_items": [],
        "mock_sourcing": {"id": "src-1", "status": "pending"},
        "expected_status": "success",
    },
    {
        "name": "No results found",
        "input": {
            "message": "Show me a red blouse",
            "customer_id": "cust-123",
            "org_id": "org-123",
            "intent_type": "item_search",
            "parsed_intent": {"color": "red", "category": "blouse"},
        },
        "mock_items": [],
        "expected_status": "no_results",
    },
]


@pytest.mark.asyncio
@pytest.mark.parametrize("case", GOLDEN_CASES, ids=lambda c: c["name"])
async def test_golden_case(case):
    tools = AsyncMock()
    tools.search_inventory = AsyncMock(return_value=case.get("mock_items", []))
    if "mock_sourcing" in case:
        tools.create_sourcing_request = AsyncMock(return_value=case["mock_sourcing"])

    agent = VisualInsightAgent(tools=tools, llm=AsyncMock())
    result = await agent.run(case["input"])

    assert result["status"] == case["expected_status"]
    if "expected_item_name" in case:
        assert result["found_items"][0]["item_name"] == case["expected_item_name"]
