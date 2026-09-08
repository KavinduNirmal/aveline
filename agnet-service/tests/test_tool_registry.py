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
    route = respx.get(f"{BASE_URL}/api/internal/customers/cust-1").respond(
        status_code=200, json={"id": "cust-1"}
    )
    registry = ToolRegistry(client)
    result = await registry.search_customer_profile("cust-1")
    assert route.called
    assert result == {"id": "cust-1"}


@pytest.mark.asyncio
@respx.mock
async def test_registry_get_customer_memories(client):
    route = respx.post(f"{BASE_URL}/api/internal/vector-search").respond(
        status_code=200, json={"results": [{"content": "likes silk"}]}
    )
    registry = ToolRegistry(client)
    result = await registry.get_customer_memories("cust-1", "what does she like?", top_k=5)
    assert route.called
    body = route.calls.last.request.content
    assert b"cust-1" in body
    assert result == {"results": [{"content": "likes silk"}]}


@pytest.mark.asyncio
@respx.mock
async def test_registry_search_inventory(client):
    route = respx.post(f"{BASE_URL}/api/internal/inventory/search").respond(
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
