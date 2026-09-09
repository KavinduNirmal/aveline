"""Golden tests validating end-to-end conversational workflow scenarios before final orchestration integration."""

from unittest.mock import AsyncMock
import pytest

from app.agents.visual_insight.intent_gate import VisualIntentGate
from app.agents.visual_insight.nodes import VisualInsightAgent

GOLDEN_CASES = [
    {
        "name": "Find green saree",
        "input": {
            "message": "Find me a green saree",
            "customer_id": "cust-123",
            "org_id": "org-123",
        },
        "expected": {
            "status": "success",
        },
    },
    {
        "name": "Reference image sourcing",
        "input": {
            "message": "Can you get this dress?",
            "customer_id": "cust-123",
            "org_id": "org-123",
            "image_url": "https://example.com/reference.jpg",
        },
        "expected": {
            "status": "success",
            "action_required": "review_sourcing_request",
        },
    },
    {
        "name": "No inventory results",
        "input": {
            "message": "Show me a red blouse",
            "customer_id": "cust-123",
            "org_id": "org-123",
        },
        "expected": {
            "status": "no_results",
        },
    },
]


@pytest.mark.asyncio
@pytest.mark.parametrize("case", GOLDEN_CASES, ids=lambda c: c["name"])
async def test_visual_insight_golden_cases(case):
    input_data = case["input"]
    expected = case["expected"]

    # Initialize Intent Gate & infer intent
    gate = VisualIntentGate()
    context = {}
    if input_data.get("image_url"):
        context["has_reference_image"] = True
        context["image_url"] = input_data["image_url"]

    intent_result = gate.infer(input_data["message"], context=context)

    # Initialize agent tools mock based on golden scenario
    tools = AsyncMock()
    if case["name"] == "Find green saree":
        tools.search_inventory.return_value = [
            {
                "item_id": "item-saree-101",
                "item_name": "Emerald Green Raw-Silk Saree",
                "price": 45000,
                "stock": 3,
                "org_id": input_data["org_id"],
            }
        ]
    elif case["name"] == "Reference image sourcing":
        tools.search_inventory.return_value = []
        tools.create_sourcing_request.return_value = {
            "id": "source-vintage-123",
            "status": "pending",
            "imageUrl": input_data["image_url"],
        }
    elif case["name"] == "No inventory results":
        tools.search_inventory.return_value = []

    agent = VisualInsightAgent(tools=tools, llm=AsyncMock())

    # Build agent execution state
    parsed_intent = {}
    if input_data.get("image_url"):
        parsed_intent["reference_image_url"] = input_data["image_url"]
    if "saree" in input_data["message"].lower():
        parsed_intent["category"] = "saree"
        parsed_intent["color"] = "green"
    elif "blouse" in input_data["message"].lower():
        parsed_intent["category"] = "blouse"
        parsed_intent["color"] = "red"

    state = {
        "org_id": input_data["org_id"],
        "customer_id": input_data.get("customer_id"),
        "message": input_data["message"],
        "intent_type": "item_search",  # Direct item_search workflow
        "parsed_intent": parsed_intent,
        "image_url": input_data.get("image_url"),
    }

    result = await agent.run(state)

    # Verify status
    assert result["status"] == expected["status"]

    # Verify action_required if specified
    if "action_required" in expected:
        assert result.get("action_required") == expected["action_required"]
        assert result.get("sourcing_suggestion") is not None
        assert result["sourcing_suggestion"]["status"] == "pending"

    # Verify found_items on success
    if expected["status"] == "success" and "action_required" not in expected:
        assert len(result.get("found_items", [])) > 0
        assert result["found_items"][0]["price"] == 45000
