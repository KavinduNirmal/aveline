from unittest.mock import AsyncMock, MagicMock
import pytest

from app.schemas.visual_insight import PieceItem
from app.tools.inventory.image_tools import analyze_product_image, parse_image_attributes_dict
from app.tools.inventory.inventory_tools import check_stock, dict_to_piece_item, search_inventory
from app.tools.inventory.matching_tools import match_customers_to_item
from app.tools.inventory.outfit_tools import compose_outfit
from app.tools.inventory.sourcing_tools import create_sourcing_request
from app.tools.inventory.supplier_tools import search_supplier_catalog


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
async def test_analyze_product_image():
    registry = MagicMock()
    registry.analyze_product_image = AsyncMock(
        return_value={
            "category": "Blazer",
            "primary_color": "Navy",
            "silhouette": "Double-breasted",
        }
    )
    res = await analyze_product_image(registry, "https://example.com/blazer.jpg")
    assert res.category == "Blazer"
    assert res.primary_color == "Navy"


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
