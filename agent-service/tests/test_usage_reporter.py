import os

import httpx
import pytest
import respx

from app.core.config import get_settings
from app.services.usage_reporter import report_usage


@pytest.fixture(autouse=True)
def _configure_settings():
    os.environ["INTERNAL_API_TOKEN"] = "test-token"
    os.environ["API_BASE_URL"] = "http://localhost:5000"
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


@pytest.mark.asyncio
@respx.mock
async def test_report_usage_success():
    expected_response = {
        "id": "0191c49b-7345-7123-8901-23456789abcd",
        "organizationId": "11111111-1111-1111-1111-111111111111",
        "workflowId": "wf-test-1",
        "provider": "openai",
        "model": "gpt-4o",
        "inputTokens": 1000,
        "outputTokens": 500,
        "cachedTokens": 100,
        "actualCostUsd": 0.005,
        "blossomUnits": 1.6,
        "createdAt": "2026-09-07T00:00:00Z",
    }

    route = respx.post("http://localhost:5000/internal/usage/record").respond(
        status_code=201, json=expected_response
    )

    result = await report_usage(
        organization_id="11111111-1111-1111-1111-111111111111",
        request_id="req-1",
        workflow_id="wf-test-1",
        provider="openai",
        model="gpt-4o",
        input_tokens=1000,
        output_tokens=500,
        cached_tokens=100,
        actual_cost_usd=0.005,
    )

    assert route.called
    request = route.calls.last.request
    assert request.headers["X-Internal-Token"] == "test-token"
    assert request.headers["Content-Type"] == "application/json"
    assert result == expected_response


@pytest.mark.asyncio
@respx.mock
async def test_report_usage_with_custom_client():
    route = respx.post("http://localhost:5000/internal/usage/record").respond(
        status_code=201, json={"success": True}
    )

    async with httpx.AsyncClient() as client:
        result = await report_usage(
            organization_id="11111111-1111-1111-1111-111111111111",
            request_id="req-custom",
            workflow_id="wf-custom",
            provider="openai",
            model="gpt-4o",
            input_tokens=200,
            output_tokens=100,
            client=client,
        )

    assert route.called
    assert result == {"success": True}


@pytest.mark.asyncio
@respx.mock
async def test_report_usage_http_error_raises():
    respx.post("http://localhost:5000/internal/usage/record").respond(
        status_code=400, json={"error": "Invalid request"}
    )

    with pytest.raises(httpx.HTTPStatusError):
        await report_usage(
            organization_id="11111111-1111-1111-1111-111111111111",
            request_id="req-err",
            workflow_id="wf-err",
            provider="openai",
            model="gpt-4o",
            input_tokens=100,
            output_tokens=100,
        )


@pytest.mark.asyncio
@respx.mock
async def test_report_usage_network_error_raises():
    respx.post("http://localhost:5000/internal/usage/record").mock(
        side_effect=httpx.ConnectError("Connection refused")
    )

    with pytest.raises(httpx.RequestError):
        await report_usage(
            organization_id="11111111-1111-1111-1111-111111111111",
            request_id="req-net-err",
            workflow_id="wf-net-err",
            provider="openai",
            model="gpt-4o",
            input_tokens=100,
            output_tokens=100,
        )
