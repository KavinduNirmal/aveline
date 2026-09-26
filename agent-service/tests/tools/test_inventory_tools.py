from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest

from app.schemas.visual_insight import PieceItem
from app.tools.inventory.image_tools import (
    AnalysisStatus,
    analyze_product_image,
    parse_image_attributes_dict,
)
from app.tools.inventory.inventory_tools import (
    check_stock,
    dict_to_piece_item,
    search_inventory,
    unwrap_items,
)
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


def test_compose_outfit_does_not_borrow_a_piece_photograph():
    """A look is a pairing, not a picture of one of its pieces.

    The composer used to hand the look the first matched piece's photo. The Salon then rendered that
    photograph twice — once on the piece and once on the look — so a one-item answer read as a
    duplicated result. A look has no photograph of its own, and now says so.
    """
    items = [
        PieceItem(
            itemId="1",
            name="Peach Raw-Silk Gown",
            price=1200.0,
            imageUrl="https://cdn/peach-gown.jpg",
        ),
        PieceItem(
            itemId="2",
            name="Pearl Drop Earrings",
            price=350.0,
            imageUrl="https://cdn/pearl-earrings.jpg",
        ),
    ]

    look = compose_outfit(items)

    assert look.imageUrl is None
    # The pieces keep theirs; only the look's borrowed copy is gone.
    assert [item.imageUrl for item in look.items] == [
        "https://cdn/peach-gown.jpg",
        "https://cdn/pearl-earrings.jpg",
    ]


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


# ---------------------------------------------------------------------------
# The camelCase wire contract (regression: a real match became "out of stock")
# ---------------------------------------------------------------------------
#
# The internal endpoint returns a bare JSON **array** of camelCase rows. The tool called `.get` on
# that array, which raised `AttributeError`, and a broad `except` turned the failure into an empty
# result. Elle then told the customer "we do not have this in stock" and raised a sourcing request -
# for a piece sitting in inventory with 10 units. Silent, and wrong in the customer's face.

#: A real row as the internal endpoint returns it.
API_ROW = {
    "id": "96050729-811b-4781-b65a-d071c8102dbf",
    "orgId": "org-1",
    "itemName": "Fuchsia Pink Bodycon Mini Dress",
    "category": "Gowns",
    "color": "Fuchsia Pink",
    "sizes": ["38", "40", "42"],
    "price": 10000.00,
    "cost": 9500.00,
    "quantity": 10,
    "status": "available",
    "imageUrl": "https://cdn.example.test/dress.jpg",
}


def test_unwrap_items_handles_the_bare_array_the_endpoint_actually_returns():
    assert unwrap_items([API_ROW]) == [API_ROW]


@pytest.mark.parametrize("key", ["items", "results", "data"])
def test_unwrap_items_accepts_each_wrapper_key(key):
    assert unwrap_items({key: [API_ROW]}) == [API_ROW]


@pytest.mark.parametrize("payload", [None, "a string", 42, {}, {"items": "not-a-list"}, []])
def test_unwrap_items_returns_nothing_for_unusable_payloads(payload):
    # A malformed envelope must not yield one junk item, which would surface to a customer as a
    # piece literally named "Boutique Piece".
    assert unwrap_items(payload) == []


def test_maps_the_camel_case_wire_fields_rather_than_defaulting_them():
    item = dict_to_piece_item(API_ROW)

    # `itemName` is the real field; missing it produced the placeholder "Boutique Piece".
    assert item.name == "Fuchsia Pink Bodycon Mini Dress"
    assert item.itemId == API_ROW["id"]
    assert item.color == "Fuchsia Pink"
    assert item.category == "Gowns"
    assert item.stock == 10
    assert item.price == 10000.00
    assert item.imageUrl == "https://cdn.example.test/dress.jpg"
    # A `sizes` array is joined rather than dropped, so a size match stays reason-able.
    assert item.size == "38, 40, 42"


def test_zero_stock_is_preserved_rather_than_treated_as_absent():
    # `stock or quantity` would let a real 0 fall through to the next source; 0 in stock is a
    # meaningful fact and must not become None.
    item = dict_to_piece_item({"itemName": "Sold Out Gown", "quantity": 0})
    assert item.stock == 0


@pytest.mark.asyncio
async def test_search_inventory_maps_a_real_camel_case_response():
    registry = MagicMock()
    registry.search_inventory = AsyncMock(return_value=[API_ROW])

    items = await search_inventory(registry, {"organizationId": "org-1", "query": "gowns"})

    assert len(items) == 1
    assert items[0].name == "Fuchsia Pink Bodycon Mini Dress"


@pytest.mark.asyncio
async def test_search_inventory_raises_on_a_transport_failure_rather_than_reporting_no_stock():
    """A failed lookup must not be indistinguishable from an empty one.

    Downstream, an empty result means "not in stock" and triggers a sourcing request. Swallowing a
    transport error therefore told customers a piece was unavailable when nobody had actually
    checked.
    """
    registry = MagicMock()
    registry.search_inventory = AsyncMock(side_effect=httpx.ConnectError("backend down"))

    with pytest.raises(httpx.ConnectError):
        await search_inventory(registry, {"organizationId": "org-1"})
