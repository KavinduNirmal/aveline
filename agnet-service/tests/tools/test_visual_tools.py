import pytest
import httpx
from app.tools.inventory.visual_tools import VisualTools


@pytest.mark.asyncio
async def test_analyze_product_image_sends_correct_request(httpx_mock):
    httpx_mock.add_response(
        method="POST",
        url="http://backend/api/internal/visual/analyze-image",
        status_code=200,
        json={
            "category": "saree",
            "primaryColor": "emerald",
            "secondaryColors": ["gold", "forest green"],
            "pattern": "floral embroidery",
            "style": "traditional luxury",
            "confidenceScore": 0.96
        }
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.analyze_product_image(
        "org-123",
        "https://example.com/saree.jpg",
        prompt="Extract visual attributes and fabric pattern"
    )

    assert result["category"] == "saree"
    assert result["primaryColor"] == "emerald"
    assert result["confidenceScore"] == 0.96


@pytest.mark.asyncio
async def test_search_inventory_sends_org_id_and_filters(httpx_mock):
    httpx_mock.add_response(
        method="POST",
        url="http://backend/api/internal/visual/search-inventory",
        status_code=200,
        json={
            "results": [
                {
                    "id": "item-1",
                    "item_name": "Emerald Saree",
                    "color": "emerald",
                    "category": "saree"
                }
            ]
        }
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    filters = {
        "color": "emerald",
        "category": "saree",
        "size": "FreeSize",
        "maximumPrice": 50000.0
    }

    result = await tools.search_inventory("org-123", filters)

    assert len(result) == 1
    assert result[0]["item_name"] == "Emerald Saree"


@pytest.mark.asyncio
async def test_get_customer_matches_calls_correct_endpoint(httpx_mock):
    httpx_mock.add_response(
        method="GET",
        url="http://backend/api/internal/visual/customer-matches/item-123?orgId=org-123&minScore=0.7",
        status_code=200,
        json=[
            {
                "customerId": "cust-1",
                "customerName": "Ananya Sharma",
                "matchScore": 0.92,
                "matchReason": "Prefers silk sarees in jewel tones"
            }
        ]
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.get_customer_matches("item-123", "org-123", min_score=0.7)

    assert len(result) == 1
    assert result[0]["customerName"] == "Ananya Sharma"
    assert result[0]["matchScore"] == 0.92


@pytest.mark.asyncio
async def test_generate_customer_matches_uses_post(httpx_mock):
    httpx_mock.add_response(
        method="POST",
        url="http://backend/api/internal/visual/customer-matches/item-123/generate",
        status_code=200,
        json=[
            {
                "customerId": "cust-2",
                "customerName": "Deepika Ranasinghe",
                "matchScore": 0.95,
                "matchReason": "High affinity for silk fabric"
            }
        ]
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.generate_customer_matches("item-123", "org-123", max_matches=5)

    assert len(result) == 1
    assert result[0]["customerName"] == "Deepika Ranasinghe"


@pytest.mark.asyncio
async def test_compose_outfit_sends_customer_and_occasion(httpx_mock):
    httpx_mock.add_response(
        method="POST",
        url="http://backend/api/internal/visual/outfits/compose",
        status_code=200,
        json={
            "lookName": "Curated Royal Luxury Ensemble",
            "style": "royal luxury",
            "occasion": "wedding",
            "primaryItem": {
                "id": "item-saree-1",
                "itemName": "Royal Sapphire Saree"
            },
            "complementaryItems": [
                {
                    "id": "item-blouse-1",
                    "itemName": "Zari Embroidered Raw Silk Blouse"
                }
            ],
            "totalLookPrice": 60000.0
        }
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.compose_outfit(
        "org-123",
        "item-saree-1",
        occasion="wedding",
        customer_id="cust-123",
        style="royal luxury"
    )

    assert result["lookName"] == "Curated Royal Luxury Ensemble"
    assert result["primaryItem"]["itemName"] == "Royal Sapphire Saree"


@pytest.mark.asyncio
async def test_create_sourcing_request_sends_reference_image(httpx_mock):
    httpx_mock.add_response(
        method="POST",
        url="http://backend/api/internal/visual/sourcing-requests",
        status_code=201,
        json={
            "id": "req-123",
            "org_id": "org-123",
            "category": "saree",
            "color": "burgundy",
            "description": "Silk Saree with gold zari",
            "referenceImageUrl": "https://example.com/reference.jpg",
            "status": "pending"
        }
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.create_sourcing_request(
        "org-123",
        {
            "category": "saree",
            "color": "burgundy",
            "description": "Silk Saree with gold zari",
            "referenceImageUrl": "https://example.com/reference.jpg",
            "targetPrice": 50000.0,
            "quantityNeeded": 2
        }
    )

    assert result["id"] == "req-123"
    assert result["category"] == "saree"
    assert result["referenceImageUrl"] == "https://example.com/reference.jpg"


@pytest.mark.asyncio
async def test_search_supplier_catalog_passes_query(httpx_mock):
    httpx_mock.add_response(
        method="GET",
        url="http://backend/api/internal/visual/suppliers/sup-123/catalog?orgId=org-123&category=saree&color=emerald&query=silk",
        status_code=200,
        json=[
            {
                "supplierId": "sup-123",
                "supplierName": "Kanchipuram Heritage Mills",
                "sku": "KHM-EM-01",
                "itemName": "Authentic Pure Zari Silk Saree",
                "category": "saree",
                "color": "emerald",
                "wholesalePrice": 38000.0,
                "leadTimeDays": 5,
                "availableStock": 20
            }
        ]
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.search_supplier_catalog(
        "sup-123",
        "org-123",
        query="silk",
        category="saree",
        color="emerald"
    )

    assert len(result) == 1
    assert result[0]["sku"] == "KHM-EM-01"


@pytest.mark.asyncio
async def test_get_item_calls_correct_endpoint(httpx_mock):
    httpx_mock.add_response(
        method="GET",
        url="http://backend/api/internal/visual/inventory/item-123?orgId=org-123",
        status_code=200,
        json={
            "id": "item-123",
            "orgId": "org-123",
            "itemName": "Royal Kanjivaram Silk",
            "price": 65000.0,
            "quantity": 4
        }
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.get_item("item-123", "org-123")

    assert result is not None
    assert result["id"] == "item-123"
    assert result["itemName"] == "Royal Kanjivaram Silk"


@pytest.mark.asyncio
async def test_backend_401_raises_error(httpx_mock):
    httpx_mock.add_response(
        method="POST",
        url="http://backend/api/internal/visual/search-inventory",
        status_code=401,
        json={"error": "Unauthorized internal service key"}
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="bad"
    )

    with pytest.raises(httpx.HTTPStatusError) as exc_info:
        await tools.search_inventory("org-123", {"category": "saree"})

    assert exc_info.value.response.status_code == 401


@pytest.mark.asyncio
async def test_backend_404_is_handled(httpx_mock):
    httpx_mock.add_response(
        method="GET",
        url="http://backend/api/internal/visual/inventory/item-nonexistent?orgId=org-123",
        status_code=404,
        json={"error": "Inventory item not found."}
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    result = await tools.get_item("item-nonexistent", "org-123")

    # Graceful handling returning None on 404
    assert result is None


@pytest.mark.asyncio
async def test_backend_500_is_handled(httpx_mock):
    httpx_mock.add_response(
        method="POST",
        url="http://backend/api/internal/visual/search-inventory",
        status_code=500,
        json={"error": "Database connection failure"}
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy"
    )

    with pytest.raises(httpx.HTTPStatusError) as exc_info:
        await tools.search_inventory("org-123", {"category": "saree"})

    assert exc_info.value.response.status_code == 500


@pytest.mark.asyncio
async def test_backend_timeout_is_handled(httpx_mock):
    httpx_mock.add_exception(
        httpx.ReadTimeout("Request timed out"),
        url="http://backend/api/internal/visual/search-inventory"
    )

    tools = VisualTools(
        api_base_url="http://backend",
        internal_api_key="dummy",
        timeout=1.0
    )

    with pytest.raises((httpx.TimeoutException, TimeoutError)):
        await tools.search_inventory("org-123", {"category": "saree"})
