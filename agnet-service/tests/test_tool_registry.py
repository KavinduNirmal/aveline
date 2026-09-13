"""Tests for the tool registry and internal API client (app/tools/).

The registry is shared infra: a thin, authenticated wrapper over backend internal
endpoints. Slice owners implement the business logic on the ASP.NET Core side;
here we only verify the client contract and routing. HTTP is mocked with respx.
"""

import os

import httpx
import pytest
import respx

from app.core.config import get_settings
from app.tools.client import InternalApiClient
from app.tools.registry import ToolRegistry

BASE_URL = "http://localhost:5000"


@pytest.fixture(autouse=True)
def _configure_settings():
    os.environ["INTERNAL_API_TOKEN"] = "test-token"
    os.environ["API_BASE_URL"] = BASE_URL
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


@pytest.fixture
def client():
    return InternalApiClient()


# ---------------------------------------------------------------------------
# InternalApiClient
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
@respx.mock
async def test_client_sends_internal_token(client):
    route = respx.get(f"{BASE_URL}/api/internal/customers/cust-1").respond(
        status_code=200, json={"id": "cust-1"}
    )
    await client.request("GET", "/api/internal/customers/cust-1")
    assert route.called
    assert route.calls.last.request.headers["X-Internal-Token"] == "test-token"


@pytest.mark.asyncio
@respx.mock
async def test_client_post_sends_json(client):
    route = respx.post(f"{BASE_URL}/api/internal/vector-search").respond(
        status_code=200, json={"results": []}
    )
    await client.request("POST", "/api/internal/vector-search", json={"query": "silk"})
    assert route.called
    assert route.calls.last.request.headers["Content-Type"] == "application/json"


@pytest.mark.asyncio
@respx.mock
async def test_client_returns_json(client):
    respx.get(f"{BASE_URL}/api/internal/customers/cust-1").respond(
        status_code=200, json={"id": "cust-1", "name": "Sarah"}
    )
    data = await client.request("GET", "/api/internal/customers/cust-1")
    assert data == {"id": "cust-1", "name": "Sarah"}


@pytest.mark.asyncio
@respx.mock
async def test_client_raises_on_http_error(client):
    respx.get(f"{BASE_URL}/api/internal/customers/cust-1").respond(
        status_code=404, json={"error": "not found"}
    )
    with pytest.raises(httpx.HTTPStatusError):
        await client.request("GET", "/api/internal/customers/cust-1")


# ---------------------------------------------------------------------------
# ToolRegistry typed stubs
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
@respx.mock
async def test_registry_search_customer_profile(client):
    route = respx.get(f"{BASE_URL}/internal/customers/cust-1/profile?organizationId=org-1").respond(
        status_code=200, json={"id": "cust-1"}
    )
    registry = ToolRegistry(client)
    result = await registry.search_customer_profile("org-1", "cust-1")
    assert route.called
    assert result == {"id": "cust-1"}


@pytest.mark.asyncio
@respx.mock
async def test_registry_get_customer_memories(client):
    route = respx.post(f"{BASE_URL}/internal/customers/memories/search").respond(
        status_code=200, json={"results": [{"content": "likes silk"}]}
    )
    registry = ToolRegistry(client)
    result = await registry.get_customer_memories("org-1", "cust-1", "what does she like?", top_k=5)
    assert route.called
    body = route.calls.last.request.content
    assert b"cust-1" in body
    assert result == {"results": [{"content": "likes silk"}]}


@pytest.mark.asyncio
@respx.mock
async def test_registry_identify_customer(client):
    route = respx.post(f"{BASE_URL}/internal/customers/identify").respond(
        status_code=200, json={"id": "cust-1", "status": "new"}
    )
    registry = ToolRegistry(client)
    result = await registry.identify_customer("org-1", "+94771234567")
    assert route.called
    body = route.calls.last.request.content
    assert b"+94771234567" in body
    assert result["status"] == "new"


@pytest.mark.asyncio
@respx.mock
async def test_registry_lookup_customers_by_name(client):
    route = respx.post(f"{BASE_URL}/internal/customers/lookup").respond(
        status_code=200, json={"matches": [{"customerId": "c1"}], "isExact": True, "total": 1}
    )
    registry = ToolRegistry(client)
    result = await registry.lookup_customers("org-1", name="Samantha Arias")
    assert route.called
    body = route.calls.last.request.content
    assert b"organizationId" in body
    assert b"name" in body
    assert b"phoneNumber" not in body
    assert result["isExact"] is True


@pytest.mark.asyncio
@respx.mock
async def test_registry_lookup_customers_by_phone(client):
    route = respx.post(f"{BASE_URL}/internal/customers/lookup").respond(
        status_code=200, json={"matches": [], "isExact": False, "total": 0}
    )
    registry = ToolRegistry(client)
    await registry.lookup_customers("org-1", phone="0771234567")
    assert route.called
    body = route.calls.last.request.content
    assert b"phoneNumber" in body
    assert b"0771234567" in body


@pytest.mark.asyncio
@respx.mock
async def test_registry_lookup_customers_by_email(client):
    route = respx.post(f"{BASE_URL}/internal/customers/lookup").respond(
        status_code=200, json={"matches": [{"customerId": "c1"}], "isExact": True, "total": 1}
    )
    registry = ToolRegistry(client)
    result = await registry.lookup_customers("org-1", email="samantha@example.com")
    assert route.called
    body = route.calls.last.request.content
    assert b"email" in body
    assert b"samantha@example.com" in body
    assert b"name" not in body
    assert result["isExact"] is True


@pytest.mark.asyncio
@respx.mock
async def test_registry_save_customer_memory(client):
    route = respx.post(f"{BASE_URL}/internal/customers/cust-1/memories").respond(
        status_code=201, json={"id": "mem-1"}
    )
    registry = ToolRegistry(client)
    result = await registry.save_customer_memory("org-1", "cust-1", "Prefers silk", "preference")
    assert route.called
    assert result == {"id": "mem-1"}


@pytest.mark.asyncio
@respx.mock
async def test_registry_generate_interaction_brief(client):
    route = respx.get(f"{BASE_URL}/internal/customers/cust-1/brief?organizationId=org-1").respond(
        status_code=200, json={"customerName": "Sarah"}
    )
    registry = ToolRegistry(client)
    result = await registry.generate_interaction_brief("org-1", "cust-1")
    assert route.called
    assert result == {"customerName": "Sarah"}


@pytest.mark.asyncio
@respx.mock
async def test_registry_record_customer_interaction(client):
    route = respx.post(f"{BASE_URL}/internal/customers/cust-1/interactions").respond(
        status_code=201, json={"id": "int-1"}
    )
    registry = ToolRegistry(client)
    result = await registry.record_customer_interaction(
        "org-1", "cust-1", "whatsapp", "inbound", "I need a blue saree",
        parsed_intent_json='{"intent_type":"item_search"}',
    )
    assert route.called
    body = route.calls.last.request.content
    assert b"parsedIntentJson" in body
    assert result == {"id": "int-1"}


@pytest.mark.asyncio
@respx.mock
async def test_registry_record_customer_interaction_omits_intent_when_none(client):
    route = respx.post(f"{BASE_URL}/internal/customers/cust-1/interactions").respond(
        status_code=201, json={"id": "int-1"}
    )
    registry = ToolRegistry(client)
    await registry.record_customer_interaction("org-1", "cust-1", "whatsapp", "inbound", "hi")
    assert route.called
    body = route.calls.last.request.content
    assert b"parsedIntentJson" not in body


@pytest.mark.asyncio
@respx.mock
async def test_registry_get_customer_consent(client):
    route = respx.get(f"{BASE_URL}/internal/customers/cust-1/consent?organizationId=org-1").respond(
        status_code=200, json={"consentStatus": "revoked"}
    )
    registry = ToolRegistry(client)
    result = await registry.get_customer_consent("org-1", "cust-1")
    assert route.called
    assert result == {"consentStatus": "revoked"}


@pytest.mark.asyncio
@respx.mock
async def test_registry_add_customer_event(client):
    route = respx.post(f"{BASE_URL}/internal/customers/cust-1/events").respond(
        status_code=201, json={"id": "evt-1", "eventType": "wedding"}
    )
    registry = ToolRegistry(client)
    result = await registry.add_customer_event("org-1", "cust-1", "wedding", "2026-12-01", "Sister's wedding")
    assert route.called
    body = route.calls.last.request.content
    assert b"eventType" in body
    assert b"eventDate" in body
    assert b"2026-12-01" in body
    assert result["eventType"] == "wedding"


@pytest.mark.asyncio
@respx.mock
async def test_registry_get_customer_events(client):
    route = respx.get(f"{BASE_URL}/internal/customers/cust-1/events?organizationId=org-1").respond(
        status_code=200, json=[{"id": "evt-1", "eventType": "wedding"}]
    )
    registry = ToolRegistry(client)
    result = await registry.get_customer_events("org-1", "cust-1")
    assert route.called
    assert result == [{"id": "evt-1", "eventType": "wedding"}]


@pytest.mark.asyncio
@respx.mock
async def test_registry_search_inventory(client):
    route = respx.post(f"{BASE_URL}/internal/visual/inventory/search").respond(
        status_code=200, json={"items": []}
    )
    registry = ToolRegistry(client)
    await registry.search_inventory({"color": "blue"})
    assert route.called


@pytest.mark.asyncio
@respx.mock
async def test_registry_calculate_margin(client):
    route = respx.post(f"{BASE_URL}/api/internal/orders/order-1/calculate-margin").respond(
        status_code=200, json={"margin": 0.2}
    )
    registry = ToolRegistry(client)
    result = await registry.calculate_margin("order-1")
    assert route.called
    assert result == {"margin": 0.2}
