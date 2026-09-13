import httpx
import pytest
import respx

from app.tools.client import InternalApiClient
from app.tools.registry import ToolRegistry


@pytest.mark.asyncio
@respx.mock
async def test_analyze_product_image_sends_correct_request():
    route = respx.post("http://backend/internal/visual/analyze-image").respond(
        status_code=200,
        json={
            "category": "saree",
            "primaryColor": "emerald",
            "secondaryColors": ["gold", "forest green"],
            "pattern": "floral embroidery",
            "style": "traditional luxury",
            "confidenceScore": 0.96,
        },
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    result = await registry.analyze_product_image(
        "https://example.com/saree.jpg",
        org_id="org-123",
    )

    assert result["category"] == "saree"
    assert result["primaryColor"] == "emerald"
    assert result["confidenceScore"] == 0.96

    assert route.called
    assert route.calls.last.request.headers.get("x-internal-token") == "secret-token"


@pytest.mark.asyncio
@respx.mock
async def test_search_inventory_sends_org_id_and_filters():
    route = respx.post("http://backend/internal/visual/inventory/search").respond(
        status_code=200,
        json=[
            {
                "id": "item-1",
                "itemName": "Emerald Saree",
                "color": "emerald",
                "category": "saree",
            }
        ],
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    result = await registry.search_inventory({
        "org_id": "org-123",
        "category": "saree",
        "color": "emerald",
        "max_price": 50000,
    })

    assert len(result) == 1
    assert result[0]["itemName"] == "Emerald Saree"
    assert route.called


@pytest.mark.asyncio
@respx.mock
async def test_get_customer_matches():
    route = respx.get(
        "http://backend/internal/visual/customer-matches/item-123?organizationId=org-123"
    ).respond(
        status_code=200,
        json=[
            {
                "customerId": "cust-1",
                "customerName": "Ananya",
                "matchScore": 0.95,
            }
        ],
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    result = await registry.match_customers_to_item("item-123", org_id="org-123")
    assert len(result) == 1
    assert result[0]["customerName"] == "Ananya"
    assert route.called


@pytest.mark.asyncio
@respx.mock
async def test_compose_outfit():
    route = respx.post("http://backend/internal/visual/outfits/compose").respond(
        status_code=200,
        json={
            "lookName": "Curated Royal Look",
            "totalLookPrice": 65000,
        },
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    result = await registry.compose_outfit({
        "org_id": "org-123",
        "primary_item_id": "item-1",
        "occasion": "wedding",
    })

    assert result["lookName"] == "Curated Royal Look"
    assert result["totalLookPrice"] == 65000
    assert route.called


@pytest.mark.asyncio
@respx.mock
async def test_create_sourcing_request():
    route = respx.post("http://backend/internal/visual/sourcing-requests").respond(
        status_code=201,
        json={
            "id": "req-123",
            "status": "pending",
        },
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    result = await registry.create_sourcing_request({
        "org_id": "org-123",
        "category": "saree",
        "color": "emerald",
    })

    assert result["id"] == "req-123"
    assert result["status"] == "pending"
    assert route.called


@pytest.mark.asyncio
@respx.mock
async def test_search_supplier_catalog():
    route = respx.get(
        "http://backend/internal/visual/suppliers/sup-123/catalog?organizationId=org-123&category=saree&color=emerald"
    ).respond(
        status_code=200,
        json=[
            {
                "sku": "KHM-01",
                "itemName": "Silk Saree",
                "wholesalePrice": 38000,
            }
        ],
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    result = await registry.search_supplier_catalog(
        supplier_id="sup-123",
        org_id="org-123",
        category="saree",
        color="emerald",
    )

    assert len(result) == 1
    assert result[0]["sku"] == "KHM-01"
    assert route.called


@pytest.mark.asyncio
@respx.mock
async def test_unauthorized_raises_error():
    respx.post("http://backend/internal/visual/inventory/search").respond(
        status_code=401,
        json={"error": "Unauthorized"},
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="bad-token",
    )
    registry = ToolRegistry(client)

    with pytest.raises(httpx.HTTPStatusError):
        await registry.search_inventory({"org_id": "org-123"})


@pytest.mark.asyncio
@respx.mock
async def test_not_found_raises_error():
    respx.get("http://backend/internal/visual/inventory/item-999?organizationId=org-123").respond(
        status_code=404,
        json={"error": "Not Found"},
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    with pytest.raises(httpx.HTTPStatusError):
        await registry.get_inventory_item("item-999", org_id="org-123")


@pytest.mark.asyncio
@respx.mock
async def test_server_error_raises_error():
    respx.post("http://backend/internal/visual/inventory/search").respond(
        status_code=500,
        json={"error": "Internal Server Error"},
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    with pytest.raises(httpx.HTTPStatusError):
        await registry.search_inventory({"org_id": "org-123"})


@pytest.mark.asyncio
@respx.mock
async def test_network_timeout_raises_request_error():
    respx.post("http://backend/internal/visual/inventory/search").mock(
        side_effect=httpx.ReadTimeout("Connection timed out")
    )

    client = InternalApiClient(
        base_url="http://backend",
        internal_token="secret-token",
    )
    registry = ToolRegistry(client)

    with pytest.raises(httpx.RequestError):
        await registry.search_inventory({"org_id": "org-123"})

