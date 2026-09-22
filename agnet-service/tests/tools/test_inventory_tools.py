from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest

from app.schemas.visual_insight import PieceItem
from app.tools.inventory.image_tools import (
    AnalysisStatus,
    analyze_product_image,
    parse_image_attributes_dict,
)
from app.tools.inventory.inventory_tools import check_stock, dict_to_piece_item, search_inventory
from app.tools.inventory.matching_tools import match_customers_to_item
from app.tools.inventory.outfit_tools import compose_outfit
from app.tools.inventory.sourcing_tools import create_sourcing_request
from app.tools.inventory.supplier_tools import search_supplier_catalog


def _status_error(status_code: int) -> httpx.HTTPStatusError:
    request = httpx.Request("POST", "http://backend/internal/visual/analyze-image")
    response = httpx.Response(status_code, request=request, json={"error": "x"})
    return httpx.HTTPStatusError("boom", request=request, response=response)



def test_parse_image_attributes_dict_keeps_neutral_and_color_fallbacks():
    """The C# side now sends primary_color; the defensive fallbacks stay (strategy §4 C15)."""
    assert parse_image_attributes_dict({}).primary_color == "Neutral"
    assert parse_image_attributes_dict({"color": "Emerald"}).primary_color == "Emerald"
    assert parse_image_attributes_dict({"primary_color": "Navy", "color": "Emerald"}).primary_color == "Navy"


def test_parse_image_attributes_dict():
    raw = {
        "category": "Saree",
        "primary_color": "Crimson",
        "secondary_colors": "Gold, Emerald",
        "fabric": "Kanjivaram Silk",
        "aesthetic_tags": "Bridal, Heritage",
    }
    attrs = parse_image_attributes_dict(raw)
    assert attrs.category == "Saree"
    assert attrs.primary_color == "Crimson"
    assert "Gold" in attrs.secondary_colors
    assert "Bridal" in attrs.aesthetic_tags


@pytest.mark.asyncio
async def test_analyze_product_image_returns_a_succeeded_typed_outcome():
    registry = MagicMock()
    registry.analyze_product_image = AsyncMock(
        return_value={
            "category": "Blazer",
            "primary_color": "Navy",
            "silhouette": "Double-breasted",
        }
    )
    res = await analyze_product_image(registry, "https://example.com/blazer.jpg", org_id="org-1")
    assert res.succeeded
    assert res.status is AnalysisStatus.SUCCEEDED
    assert res.attributes is not None
    assert res.attributes.category == "Blazer"
    assert res.attributes.primary_color == "Navy"


@pytest.mark.asyncio
async def test_analyze_product_image_forwards_the_reference_and_the_organization():
    registry = MagicMock()
    registry.analyze_product_image = AsyncMock(
        return_value={"category": "Blazer", "primary_color": "Navy"}
    )
    await analyze_product_image(
        registry,
        image_url="https://bridge.example/api/v1/media/token",
        org_id="org-1",
        image_ref_kind="attachment",
        image_ref_id="att-1",
    )
    registry.analyze_product_image.assert_awaited_once_with(
        image_url="https://bridge.example/api/v1/media/token",
        org_id="org-1",
        image_ref_kind="attachment",
        image_ref_id="att-1",
    )


@pytest.mark.asyncio
async def test_analyze_product_image_denied_is_distinguishable_from_failed():
    """A refusal (4xx) is DENIED; an unreachable/stale backend is FAILED. They are not one outcome."""
    denied_registry = MagicMock()
    denied_registry.analyze_product_image = AsyncMock(side_effect=_status_error(404))
    denied = await analyze_product_image(denied_registry, "https://x/y.jpg", org_id="org-1")

    failed_registry = MagicMock()
    failed_registry.analyze_product_image = AsyncMock(side_effect=httpx.ConnectError("refused"))
    failed = await analyze_product_image(failed_registry, "https://x/y.jpg", org_id="org-1")

    assert denied.status is AnalysisStatus.DENIED
    assert denied.status_code == 404
    assert denied.attributes is None
    assert failed.status is AnalysisStatus.FAILED
    assert failed.attributes is None
    assert denied.status is not failed.status


@pytest.mark.asyncio
async def test_analyze_product_image_server_error_is_failed_not_denied():
    registry = MagicMock()
    registry.analyze_product_image = AsyncMock(side_effect=_status_error(503))
    outcome = await analyze_product_image(registry, "https://x/y.jpg", org_id="org-1")
    assert outcome.status is AnalysisStatus.FAILED
    assert outcome.status_code == 503


@pytest.mark.asyncio
async def test_analyze_product_image():
    registry = MagicMock()
    registry.analyze_product_image = AsyncMock(
        return_value={
            "category": "Blazer",
            "primary_color": "Navy",
            "silhouette": "Double-breasted",
        }
    )
    res = await analyze_product_image(registry, "https://example.com/blazer.jpg", org_id="org-1")
    assert res.attributes.category == "Blazer"
    assert res.attributes.primary_color == "Navy"


def test_dict_to_piece_item():
    raw = {
        "id": "piece-99",
        "name": "Linen Tailored Trouser",
        "price": "320.50",
        "quantity": "5",
        "size": "M",
    }
    item = dict_to_piece_item(raw)
    assert item.itemId == "piece-99"
    assert item.name == "Linen Tailored Trouser"
    assert item.price == 320.50
    assert item.stock == 5


@pytest.mark.asyncio
async def test_search_inventory():
    registry = MagicMock()
    registry.search_inventory = AsyncMock(
        return_value={
            "items": [
                {"id": "1", "name": "Silk Wrap Dress", "price": 650.0, "stock": 2},
                {"id": "2", "name": "Cashmere Wrap", "price": 400.0, "stock": 4},
            ]
        }
    )
    items = await search_inventory(registry, {"query": "wrap", "organizationId": "org-1"})
    assert len(items) == 2
    assert items[0].name == "Silk Wrap Dress"


@pytest.mark.asyncio
async def test_check_stock():
    registry = MagicMock()
    registry.check_stock = AsyncMock(return_value={"itemId": "item-1", "stock": 3, "inStock": True})
    stock_info = await check_stock(registry, "item-1", "org-1")
    assert stock_info["stock"] == 3
    assert stock_info["inStock"] is True


def test_compose_outfit():
    items = [
        PieceItem(itemId="1", name="Peach Raw-Silk Gown", price=1200.0),
        PieceItem(itemId="2", name="Pearl Drop Earrings", price=350.0),
    ]
    look = compose_outfit(items, occasion="Galle Beach Wedding")
    assert "Galle Beach Wedding" in look.name
    assert "Peach Raw-Silk Gown" in look.text
    assert len(look.items) == 2


@pytest.mark.asyncio
async def test_create_sourcing_request():
    registry = MagicMock()
    registry.create_sourcing_request = AsyncMock(return_value={"requestId": "src_123"})
    req = await create_sourcing_request(
        registry,
        org_id="org-1",
        customer_id="cust-1",
        image_url="https://img.com/gown.png",
        notes="Custom ivory tone needed",
    )
    assert req.requestId == "src_123"
    assert req.status == "pending"


@pytest.mark.asyncio
async def test_match_customers_to_item():
    registry = MagicMock()
    registry.match_customers_to_item = AsyncMock(
        return_value={"matches": [{"customerId": "c1", "name": "Sarah", "score": 0.95}]}
    )
    matches = await match_customers_to_item(registry, "item-1", "org-1")
    assert len(matches) == 1
    assert matches[0]["name"] == "Sarah"


@pytest.mark.asyncio
async def test_search_supplier_catalog():
    registry = MagicMock()
    registry.search_supplier_catalog = AsyncMock(
        return_value={"items": [{"supplier": "Como Silks", "material": "Mulberry Silk"}]}
    )
    items = await search_supplier_catalog(registry, "Mulberry Silk", "org-1")
    assert len(items) == 1
    assert items[0]["supplier"] == "Como Silks"
