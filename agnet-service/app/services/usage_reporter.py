import logging
from typing import Any

import httpx

from app.core.config import get_settings

logger = logging.getLogger("aveline.agent.usage_reporter")


async def report_usage(
    organization_id: str,
    request_id: str,
    workflow_id: str,
    provider: str,
    model: str,
    input_tokens: int,
    output_tokens: int,
    cached_tokens: int = 0,
    actual_cost_usd: float = 0.0,
    *,
    client: httpx.AsyncClient | None = None,
) -> dict[str, Any] | None:
    """Submit AI usage for a completed agent workflow to the .NET API.

    Blossom units are never calculated client-side — they are determined by
    the .NET backend's UsageTrackerService (ADR-010).

    Args:
        organization_id: Organization GUID string.
        request_id: Correlation ID tracing the workflow request.
        workflow_id: LangGraph thread/workflow ID.
        provider: AI provider identifier (e.g. 'openai').
        model: Specific model used (e.g. 'gpt-4o').
        input_tokens: Total prompt tokens consumed.
        output_tokens: Total completion tokens generated.
        cached_tokens: Cached prompt tokens used.
        actual_cost_usd: Raw provider cost in USD.
        client: Optional httpx.AsyncClient for connection reuse / testing.

    Returns:
        JSON response dict from the API if successful, or None if failed.
    """
    settings = get_settings()
    endpoint = f"{settings.api_base_url.rstrip('/')}/internal/usage/record"

    headers = {
        "Content-Type": "application/json",
        "X-Internal-Token": settings.internal_api_token,
    }

    payload = {
        "organizationId": organization_id,
        "requestId": request_id,
        "workflowId": workflow_id,
        "provider": provider,
        "model": model,
        "inputTokens": input_tokens,
        "outputTokens": output_tokens,
        "cachedTokens": cached_tokens,
        "actualCostUsd": actual_cost_usd,
    }

    should_close_client = False
    if client is None:
        client = httpx.AsyncClient(timeout=10.0)
        should_close_client = True

    try:
        response = await client.post(endpoint, json=payload, headers=headers)
        response.raise_for_status()
        data = response.json()
        logger.info(
            "Usage reported successfully",
            extra={
                "organization_id": organization_id,
                "workflow_id": workflow_id,
                "model": model,
            },
        )
        return data
    except httpx.HTTPStatusError as exc:
        logger.error(
            "HTTP error reporting usage to backend: %s - %s",
            exc.response.status_code,
            exc.response.text,
            extra={
                "organization_id": organization_id,
                "workflow_id": workflow_id,
                "status_code": exc.response.status_code,
            },
        )
        raise
    except httpx.RequestError as exc:
        logger.error(
            "Network error reporting usage to backend: %s",
            str(exc),
            extra={
                "organization_id": organization_id,
                "workflow_id": workflow_id,
            },
        )
        raise
    finally:
        if should_close_client:
            await client.aclose()
