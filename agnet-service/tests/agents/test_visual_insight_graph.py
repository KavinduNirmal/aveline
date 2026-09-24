import json
from unittest.mock import AsyncMock, MagicMock

import pytest
import respx

from app.agents.visual_insight.graph import build_visual_graph
from app.agents.visual_insight.nodes import VisualInsightAgent
from app.tools.client import InternalApiClient
from app.tools.registry import ToolRegistry

REAL_ORG = "11111111-2222-3333-4444-555555555555"
REAL_ATTACHMENT = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
ALL_ZEROS = "00000000-0000-0000-0000-000000000000"


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
    # The look names no photograph: borrowing the gown's would show the same picture twice in the
    # Salon, once as the piece and once as the look.
    assert output["looks"][0]["imageUrl"] is None
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


@pytest.mark.asyncio
async def test_visual_insight_graph_with_llm_curation():
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
                }
            ]
        }
    )

    mock_llm_response = MagicMock()
    mock_llm_response.content = "Custom editorial look curated by AI stylist."
    mock_llm_response.usage_metadata = {"input_tokens": 120, "output_tokens": 45}

    mock_llm = MagicMock()
    mock_llm.ainvoke = AsyncMock(return_value=mock_llm_response)

    graph = build_visual_graph(registry, llm=mock_llm)

    state = {
        "org_id": "org-boutique-1",
        "customer_id": "cust-sarah",
        "message": "I need an outfit for my sister's wedding in Galle",
        "image_url": None,
    }

    result = await graph.ainvoke(state)
    output = result["output"]

    assert output["status"] == "success"
    assert len(output["looks"]) == 1
    assert output["looks"][0]["text"] == "Custom editorial look curated by AI stylist."
    assert mock_llm.ainvoke.called


@pytest.mark.asyncio
async def test_visual_insight_graph_staff_query_emits_text_summary_no_suggestion():
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
                }
            ]
        }
    )

    graph = build_visual_graph(registry)

    state = {
        "org_id": "org-boutique-1",
        "customer_id": "cust-sarah",
        "message": "Stock check for Peach Gown",
        "staff_query": True,
        "direction": "outbound",
    }

    result = await graph.ainvoke(state)
    output = result["output"]

    assert output["status"] == "success"
    assert output["suggestion"] is None
    assert output["text"] is not None
    assert "Found 1 matching inventory item(s)" in output["text"]


def test_coerce_visual_output_runtime_validation():
    from pydantic import ValidationError

    from app.schemas.visual_insight import coerce_visual_output

    valid_payload = {
        "status": "success",
        "agent": "visual",
        "ran": True,
        "items": [],
        "looks": [],
        "suggestion": "Elle curated 1 piece.",
    }
    validated = coerce_visual_output(valid_payload)
    assert validated.status == "success"
    assert validated.suggestion == "Elle curated 1 piece."

    invalid_payload = {
        **valid_payload,
        "forbidden_extra_field": "disallowed",
    }
    with pytest.raises(ValidationError):
        coerce_visual_output(invalid_payload)


def test_dead_non_graph_entrypoints_are_gone():
    """U3.1 deletes the rule-based non-graph path rather than porting it (strategy §1, plan §4).

    ``graph.py`` never wired ``run`` or its ``_handle_*`` siblings; the image-analysis branch
    carried an unbound call. The path is gone, so its absence is asserted here.
    """
    for name in ("run", "_handle_image_analysis", "_handle_outfit_composition", "_handle_item_search"):
        assert not hasattr(VisualInsightAgent, name), f"{name} is the deleted non-graph path"


def _registry() -> ToolRegistry:
    return ToolRegistry(InternalApiClient(base_url="http://backend", internal_token="secret-token"))


@pytest.mark.asyncio
@respx.mock
async def test_graph_sends_reference_with_real_organization_not_all_zeros():
    """The graph route posts the reference and the real tenant, never the all-zeros sentinel."""
    analyze_route = respx.post("http://backend/internal/visual/analyze-image").respond(
        status_code=200,
        json={"category": "Cocktail Dress", "primary_color": "Emerald"},
    )
    respx.post("http://backend/internal/visual/inventory/search").respond(
        status_code=200,
        json={"items": [{"itemId": "item-1", "name": "Emerald Slip", "price": 750.0, "stock": 1}]},
    )

    graph = build_visual_graph(_registry())
    await graph.ainvoke({
        "org_id": REAL_ORG,
        "message": "Do you have anything matching this photo?",
        "image_url": "https://bridge.example/api/v1/media/rotating-token",
        "image_ref_kind": "attachment",
        "image_ref_id": REAL_ATTACHMENT,
    })

    assert analyze_route.called
    body = json.loads(analyze_route.calls.last.request.content)
    assert body["organizationId"] == REAL_ORG
    assert body["organizationId"] != ALL_ZEROS
    assert body["imageRefKind"] == "attachment"
    assert body["imageRefId"] == REAL_ATTACHMENT
    assert "imageUrl" not in body


@pytest.mark.asyncio
@respx.mock
async def test_graph_legacy_image_url_arm_still_works():
    """With no reference present the graph still analyses the legacy absolute URL."""
    analyze_route = respx.post("http://backend/internal/visual/analyze-image").respond(
        status_code=200,
        json={"category": "Cocktail Dress", "primary_color": "Emerald"},
    )
    respx.post("http://backend/internal/visual/inventory/search").respond(
        status_code=200,
        json={"items": []},
    )
    respx.post("http://backend/internal/visual/sourcing-requests").respond(
        status_code=200,
        json={"requestId": "src-1", "status": "pending"},
    )

    graph = build_visual_graph(_registry())
    await graph.ainvoke({
        "org_id": REAL_ORG,
        "message": "Do you have anything matching this photo?",
        "image_url": "https://example.com/legacy.jpg",
    })

    assert analyze_route.called
    body = json.loads(analyze_route.calls.last.request.content)
    assert body["organizationId"] == REAL_ORG
    assert body["imageUrl"] == "https://example.com/legacy.jpg"
    assert "imageRefKind" not in body
    assert "imageRefId" not in body


@pytest.mark.asyncio
async def test_graph_refuses_to_analyse_without_an_organization():
    """No organisation means no request: the all-zeros sentinel is unreachable."""
    registry = MagicMock()
    registry.analyze_product_image = AsyncMock(return_value={"category": "Dress", "primary_color": "Red"})
    registry.search_inventory = AsyncMock(return_value={"items": []})
    registry.create_sourcing_request = AsyncMock(return_value={"requestId": "src-1"})

    graph = build_visual_graph(registry)
    result = await graph.ainvoke({
        "org_id": "",
        "message": "match this",
        "image_url": "https://example.com/x.jpg",
    })

    registry.analyze_product_image.assert_not_called()
    assert result["output"]["image_attributes"] is None


@pytest.mark.asyncio
@respx.mock
async def test_analyze_image_node_surfaces_a_denied_analysis_distinctly():
    respx.post("http://backend/internal/visual/analyze-image").respond(
        status_code=403, json={"error": "forbidden"}
    )
    agent = VisualInsightAgent(_registry())

    update = await agent.analyze_image({
        "org_id": REAL_ORG,
        "image_ref_kind": "attachment",
        "image_ref_id": REAL_ATTACHMENT,
        "search_criteria": {},
    })

    assert update["image_attributes"] is None
    assert update["reason"] == "image_analysis_denied"


@pytest.mark.asyncio
@respx.mock
async def test_analyze_image_node_surfaces_a_failed_analysis_distinctly():
    respx.post("http://backend/internal/visual/analyze-image").respond(
        status_code=503, json={"error": "unavailable"}
    )
    agent = VisualInsightAgent(_registry())

    update = await agent.analyze_image({
        "org_id": REAL_ORG,
        "image_ref_kind": "attachment",
        "image_ref_id": REAL_ATTACHMENT,
        "search_criteria": {},
    })

    assert update["image_attributes"] is None
    assert update["reason"] == "image_analysis_failed"



# ---------------------------------------------------------------------------
# Conversation-context transport (ADR-023) — the window must reach the styling prompt.
# ---------------------------------------------------------------------------


class _CapturingVisualLlm:
    """A chat-model double that records the message list it was invoked with."""

    def __init__(self) -> None:
        self.calls: list[list] = []

    async def ainvoke(self, messages):
        self.calls.append(messages)
        response = MagicMock()
        response.content = "Custom editorial look curated by AI stylist."
        response.usage_metadata = {"input_tokens": 120, "output_tokens": 45}
        return response

    def prompt_text(self) -> str:
        return "\n".join(str(part) for part in self.calls[0])


@pytest.mark.asyncio
async def test_visual_history_reaches_the_styling_prompt():
    """The bounded window is rendered into the styling system prompt, not only the supervisor's."""
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
                }
            ]
        }
    )
    llm = _CapturingVisualLlm()
    graph = build_visual_graph(registry, llm=llm)

    state = {
        "org_id": REAL_ORG,
        "message": "the pink one",
        "history": [
            {"authorKind": "Customer", "text": "Any pinkish gowns?"},
            {"authorKind": "Agent", "text": "We have three."},
        ],
        "thread_summary": "She is shopping for a December wedding.",
        "pinned_slots": {"budget": "50k"},
    }

    result = await graph.ainvoke(state)

    assert result["output"]["status"] == "success"
    assert llm.calls, "the LLM was never invoked, so there is no prompt to inspect"
    prompt = llm.prompt_text()
    assert "Any pinkish gowns?" in prompt
    assert "December wedding" in prompt
    assert "budget: 50k" in prompt
